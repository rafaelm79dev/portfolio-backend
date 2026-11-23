using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Messaging;

namespace Orders.Tests;

[Collection("postgres")]
public class StockResultHandlerTests(PostgresFixture postgres)
{
    private async Task<(ServiceProvider Provider, StockResultHandler Handler)> CreateAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"orders_{Guid.NewGuid():N}"
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContext<OrdersDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        var provider = services.BuildServiceProvider();

        using (var scope = provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().Database.MigrateAsync();

        return (provider, NewHandler(provider, NullLogger<StockResultHandler>.Instance));
    }

    private static StockResultHandler NewHandler(ServiceProvider provider, ILogger<StockResultHandler> logger) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, logger);

    private static async Task<Guid> AddPendingOrderAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = Order.Create([new OrderLine(Guid.NewGuid(), 1)], DateTimeOffset.UtcNow);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return order.Id;
    }

    private static async Task<Order> LoadOrderAsync(ServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await db.Orders.AsNoTracking().SingleAsync(o => o.Id == id);
    }

    private static MessageEnvelope<JsonElement> Reserved(Guid orderId, Guid? messageId = null) =>
        new(messageId ?? Guid.NewGuid(), EventTypes.StockReserved, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new StockReserved(orderId), ContractJson.Options));

    private static MessageEnvelope<JsonElement> Rejected(Guid orderId, string reason, Guid? messageId = null) =>
        new(messageId ?? Guid.NewGuid(), EventTypes.StockRejected, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new StockRejected(orderId, reason), ContractJson.Options));

    [Fact]
    public async Task StockReserved_confirms_order()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);

        await handler.HandleAsync(Reserved(orderId), CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, (await LoadOrderAsync(provider, orderId)).Status);
    }

    [Fact]
    public async Task StockRejected_rejects_order_with_reason()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);

        await handler.HandleAsync(Rejected(orderId, "Insufficient stock for: Monitor"), CancellationToken.None);

        var order = await LoadOrderAsync(provider, orderId);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("Insufficient stock for: Monitor", order.RejectionReason);
    }

    [Fact]
    public async Task Very_long_rejection_reason_still_rejects_the_order()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);

        await handler.HandleAsync(Rejected(orderId, new string('r', 2000)), CancellationToken.None);

        var order = await LoadOrderAsync(provider, orderId);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(Order.MaxReasonLength, order.RejectionReason!.Length);
    }

    [Fact]
    public async Task Same_message_twice_is_applied_once()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);
        var message = Reserved(orderId);

        await handler.HandleAsync(message, CancellationToken.None);
        var afterFirst = await LoadOrderAsync(provider, orderId);
        await handler.HandleAsync(message, CancellationToken.None);
        var afterSecond = await LoadOrderAsync(provider, orderId);

        Assert.Equal(OrderStatus.Confirmed, afterSecond.Status);
        Assert.Equal(afterFirst.UpdatedAt, afterSecond.UpdatedAt);
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.Equal(1, await db.ProcessedMessages.CountAsync());
    }

    [Fact]
    public async Task Late_reply_does_not_change_an_order_that_is_already_decided()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);
        await handler.HandleAsync(Rejected(orderId, "no stock"), CancellationToken.None);

        await handler.HandleAsync(Reserved(orderId), CancellationToken.None);

        var order = await LoadOrderAsync(provider, orderId);
        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal("no stock", order.RejectionReason);
    }

    [Fact]
    public async Task A_late_reply_that_changes_nothing_is_logged_as_a_warning()
    {
        var (provider, _) = await CreateAsync();
        await using var _1 = provider;
        var logger = new ListLogger<StockResultHandler>();
        var handler = NewHandler(provider, logger);
        var orderId = await AddPendingOrderAsync(provider);
        await handler.HandleAsync(Rejected(orderId, "no stock"), CancellationToken.None);

        await handler.HandleAsync(Reserved(orderId), CancellationToken.None);

        var warning = Assert.Single(logger.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(orderId.ToString(), warning.Message);
    }

    [Fact]
    public async Task A_reply_that_changes_the_order_is_not_logged_as_a_warning()
    {
        var (provider, _) = await CreateAsync();
        await using var _1 = provider;
        var logger = new ListLogger<StockResultHandler>();
        var handler = NewHandler(provider, logger);
        var orderId = await AddPendingOrderAsync(provider);

        await handler.HandleAsync(Reserved(orderId), CancellationToken.None);

        Assert.DoesNotContain(logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Unknown_order_throws()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.HandleAsync(Reserved(Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Unreadable_payload_throws_poison_message()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var broken = new MessageEnvelope<JsonElement>(
            Guid.NewGuid(), EventTypes.StockReserved, DateTimeOffset.UtcNow,
            JsonSerializer.SerializeToElement(new { orderId = "not-a-guid" }));

        await Assert.ThrowsAsync<PoisonMessageException>(() => handler.HandleAsync(broken, CancellationToken.None));
    }

    [Fact]
    public async Task Ignores_event_types_it_does_not_handle()
    {
        var (provider, handler) = await CreateAsync();
        await using var _ = provider;
        var orderId = await AddPendingOrderAsync(provider);
        var other = new MessageEnvelope<JsonElement>(
            Guid.NewGuid(), "SomethingElse", DateTimeOffset.UtcNow, JsonSerializer.SerializeToElement(new { }));

        await handler.HandleAsync(other, CancellationToken.None);

        Assert.Equal(OrderStatus.Pending, (await LoadOrderAsync(provider, orderId)).Status);
    }
}
