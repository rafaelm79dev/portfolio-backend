using System.Text.Json;
using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.Tests;

[Collection("postgres")]
public class OrderCreatedHandlerTests(PostgresFixture postgres)
{
    private async Task<(ServiceProvider Provider, OrderCreatedHandler Handler)> CreateAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"inventory_{Guid.NewGuid():N}"
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ReservationService>();
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();

        return (provider, new OrderCreatedHandler(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System));
    }

    private static MessageEnvelope<JsonElement> OrderCreatedMessage(
        Guid orderId, Guid messageId, params (Guid productId, int quantity)[] lines) =>
        new(messageId, EventTypes.OrderCreated, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(
                new OrderCreated(orderId, lines.Select(l => new OrderLine(l.productId, l.quantity)).ToList()),
                ContractJson.Options));

    private static async Task<int> StockOfAsync(ServiceProvider provider, Guid productId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Stock;
    }

    private static async Task<List<OutboxMessage>> OutboxAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.OutboxMessages.AsNoTracking().ToListAsync();
    }

    [Fact]
    public async Task Reserved_order_writes_stock_reserved_outbox_row()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        await handler.HandleAsync(OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Laptop, 2)), CancellationToken.None);

        var row = Assert.Single(await OutboxAsync(provider));
        Assert.Equal(Topics.InventoryEvents, row.Topic);
        Assert.Equal(orderId.ToString(), row.Key);
        Assert.Equal(EventTypes.StockReserved, row.Type);
        Assert.Null(row.PublishedAt);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<StockReserved>>(row.Payload, ContractJson.Options)!;
        Assert.Equal(row.Id, envelope.MessageId);
        Assert.Equal(orderId, envelope.Payload.OrderId);
        Assert.Equal(before - 2, await StockOfAsync(provider, SeedData.Laptop));
    }

    [Fact]
    public async Task Insufficient_stock_writes_stock_rejected_with_reason()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Monitor);

        await handler.HandleAsync(
            OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Monitor, before + 1)), CancellationToken.None);

        var row = Assert.Single(await OutboxAsync(provider));
        Assert.Equal(EventTypes.StockRejected, row.Type);
        var envelope = JsonSerializer.Deserialize<MessageEnvelope<StockRejected>>(row.Payload, ContractJson.Options)!;
        Assert.Equal(orderId, envelope.Payload.OrderId);
        Assert.Contains("stock", envelope.Payload.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await StockOfAsync(provider, SeedData.Monitor));
    }

    [Fact]
    public async Task Same_message_twice_decrements_stock_once_and_writes_one_outbox_row()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var message = OrderCreatedMessage(Guid.NewGuid(), Guid.NewGuid(), (SeedData.Laptop, 2));
        var before = await StockOfAsync(provider, SeedData.Laptop);

        await handler.HandleAsync(message, CancellationToken.None);
        await handler.HandleAsync(message, CancellationToken.None);

        Assert.Single(await OutboxAsync(provider));
        Assert.Equal(before - 2, await StockOfAsync(provider, SeedData.Laptop));
    }

    [Fact]
    public async Task Another_message_for_the_same_order_does_not_reserve_stock_again()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        await handler.HandleAsync(OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Laptop, 2)), CancellationToken.None);
        await handler.HandleAsync(OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Laptop, 2)), CancellationToken.None);

        Assert.Equal(before - 2, await StockOfAsync(provider, SeedData.Laptop));
        var rows = await OutboxAsync(provider);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(EventTypes.StockReserved, r.Type));
    }

    [Fact]
    public async Task Another_message_for_a_rejected_order_gets_the_same_rejection_and_takes_no_stock()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Monitor);

        await handler.HandleAsync(OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Monitor, before + 1)), CancellationToken.None);
        await handler.HandleAsync(OrderCreatedMessage(orderId, Guid.NewGuid(), (SeedData.Monitor, 1)), CancellationToken.None);

        Assert.Equal(before, await StockOfAsync(provider, SeedData.Monitor));
        var rows = await OutboxAsync(provider);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(EventTypes.StockRejected, r.Type));
    }

    [Fact]
    public async Task Ignores_event_types_it_does_not_handle()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var other = new MessageEnvelope<JsonElement>(
            Guid.NewGuid(), "SomethingElse", DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }));

        await handler.HandleAsync(other, CancellationToken.None);

        Assert.Empty(await OutboxAsync(provider));
    }

    [Fact]
    public async Task Unreadable_payload_throws_poison_message_and_changes_nothing()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var broken = new MessageEnvelope<JsonElement>(
            Guid.NewGuid(), EventTypes.OrderCreated, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { orderId = "not-a-guid" }));

        await Assert.ThrowsAsync<PoisonMessageException>(() => handler.HandleAsync(broken, CancellationToken.None));

        Assert.Empty(await OutboxAsync(provider));
    }
}
