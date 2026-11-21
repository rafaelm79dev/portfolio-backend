using System.Text.Json;
using Contracts;

namespace Orders.Tests;

public class ContractsTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Envelope_roundtrips_through_json()
    {
        var orderId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var envelope = new MessageEnvelope<OrderCreated>(
            Guid.NewGuid(),
            EventTypes.OrderCreated,
            DateTimeOffset.UtcNow,
            new OrderCreated(orderId, [new OrderLine(productId, 2)]));

        var text = JsonSerializer.Serialize(envelope, Json);
        var back = JsonSerializer.Deserialize<MessageEnvelope<OrderCreated>>(text, Json)!;

        Assert.Equal(envelope.MessageId, back.MessageId);
        Assert.Equal("OrderCreated", back.Type);
        Assert.Equal(orderId, back.Payload.OrderId);
        Assert.Single(back.Payload.Items);
        Assert.Equal(productId, back.Payload.Items[0].ProductId);
        Assert.Equal(2, back.Payload.Items[0].Quantity);
    }

    [Fact]
    public void Dlq_appends_suffix()
    {
        Assert.Equal("orders.events.dlq", Topics.Dlq(Topics.OrdersEvents));
    }
}
