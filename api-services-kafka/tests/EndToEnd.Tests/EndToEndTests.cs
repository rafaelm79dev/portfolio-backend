using System.Net;
using System.Net.Http.Json;
using Confluent.Kafka;
using Contracts;

namespace EndToEnd.Tests;

[Collection("stack")]
public class EndToEndTests(StackFixture stack)
{
    // Same ids as the seed data in Inventory.Api (Laptop 10 units, Mouse 50, Keyboard 30, Monitor 5).
    private static readonly Guid Laptop = Guid.Parse("a1b2c3d4-0001-4000-8000-000000000001");
    private static readonly Guid Mouse = Guid.Parse("a1b2c3d4-0002-4000-8000-000000000002");
    private static readonly Guid Keyboard = Guid.Parse("a1b2c3d4-0003-4000-8000-000000000003");
    private static readonly Guid Monitor = Guid.Parse("a1b2c3d4-0004-4000-8000-000000000004");

    private sealed record CreatedResponse(Guid OrderId);

    private sealed record OrderResponse(Guid Id, string Status, string? RejectionReason);

    private sealed record ProductResponse(Guid Id, string Name, int Stock);

    private async Task<Guid> PlaceOrderAsync(params (Guid productId, int quantity)[] lines)
    {
        var response = await stack.Orders.PostAsJsonAsync("/orders", new
        {
            items = lines.Select(l => new { productId = l.productId, quantity = l.quantity })
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreatedResponse>())!.OrderId;
    }

    private async Task<OrderResponse> GetOrderAsync(Guid id) =>
        (await stack.Orders.GetFromJsonAsync<OrderResponse>($"/orders/{id}"))!;

    private async Task<OrderResponse> WaitForDecisionAsync(Guid id, int seconds = 60)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        OrderResponse order;
        do
        {
            order = await GetOrderAsync(id);
            if (order.Status != "Pending") return order;
            await Task.Delay(250);
        } while (DateTime.UtcNow < deadline);

        Assert.Fail($"Order {id} was still Pending after {seconds}s");
        return order;
    }

    private async Task<int> StockAsync(Guid productId) =>
        (await stack.Inventory.GetFromJsonAsync<ProductResponse>($"/products/{productId}"))!.Stock;

    [Fact]
    public async Task Order_with_enough_stock_ends_confirmed()
    {
        var before = await StockAsync(Laptop);

        var orderId = await PlaceOrderAsync((Laptop, 1));
        var order = await WaitForDecisionAsync(orderId);

        Assert.Equal("Confirmed", order.Status);
        Assert.Equal(before - 1, await StockAsync(Laptop));
    }

    [Fact]
    public async Task Order_without_enough_stock_ends_rejected()
    {
        var before = await StockAsync(Monitor);

        var orderId = await PlaceOrderAsync((Monitor, before + 1));
        var order = await WaitForDecisionAsync(orderId);

        Assert.Equal("Rejected", order.Status);
        Assert.Contains("stock", order.RejectionReason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await StockAsync(Monitor));
    }

    [Fact]
    public async Task Duplicate_order_created_message_does_not_double_decrement_stock()
    {
        var before = await StockAsync(Keyboard);
        var orderId = await PlaceOrderAsync((Keyboard, 3));
        Assert.Equal("Confirmed", (await WaitForDecisionAsync(orderId)).Status);
        var original = await stack.ReadOutboxPayloadAsync(orderId.ToString());

        // Deliver the very same message (same messageId) a second time.
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = stack.KafkaBootstrap, Acks = Acks.All }).Build();
        await producer.ProduceAsync(Topics.OrdersEvents,
            new Message<string, string> { Key = orderId.ToString(), Value = original });

        // A later order going through shows the consumer is alive and has had time to see the duplicate.
        var later = await PlaceOrderAsync((Mouse, 1));
        await WaitForDecisionAsync(later);
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(before - 3, await StockAsync(Keyboard));
        Assert.Equal("Confirmed", (await GetOrderAsync(orderId)).Status);
    }

    [Fact]
    public async Task Order_created_while_kafka_is_down_is_confirmed_after_recovery()
    {
        await stack.StopKafkaAsync();
        try
        {
            var orderId = await PlaceOrderAsync((Mouse, 1)); // accepted: only the database is needed
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.Equal("Pending", (await GetOrderAsync(orderId)).Status);

            await stack.StartKafkaAsync();

            Assert.Equal("Confirmed", (await WaitForDecisionAsync(orderId, seconds: 120)).Status);
        }
        finally
        {
            // Never leave the shared broker stopped for the other tests (no-op if it is already running).
            try { await stack.StartKafkaAsync(); } catch { /* already running */ }
        }
    }

    [Fact]
    public async Task Malformed_message_in_orders_topic_lands_in_dlq()
    {
        var garbage = $"garbage-{Guid.NewGuid():N}";
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = stack.KafkaBootstrap, Acks = Acks.All }).Build();
        await producer.ProduceAsync(Topics.OrdersEvents, new Message<string, string> { Key = "k", Value = garbage });

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = stack.KafkaBootstrap,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.Dlq(Topics.OrdersEvents));

        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                Thread.Sleep(200); // the DLQ topic does not exist until something is dead-lettered
                continue;
            }

            if (result?.Message.Value == garbage) return;
        }

        Assert.Fail("The malformed message never reached orders.events.dlq");
    }
}
