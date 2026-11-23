using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.Tests;

[Collection("postgres")]
public class ReservationServiceTests(PostgresFixture postgres)
{
    private async Task<ServiceProvider> CreateDatabaseAsync()
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

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
        return provider;
    }

    /// <summary>Runs one reservation the way the consumer will: inside a transaction, committed afterwards.</summary>
    private static Task<ReservationResult> ReserveAsync(
        ServiceProvider provider, params (Guid productId, int quantity)[] lines) =>
        ReserveForOrderAsync(provider, Guid.NewGuid(), lines);

    private static async Task<ReservationResult> ReserveForOrderAsync(
        ServiceProvider provider, Guid orderId, params (Guid productId, int quantity)[] lines)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ReservationService>().ReserveAsync(
            orderId, lines.Select(l => new OrderLine(l.productId, l.quantity)).ToList(), CancellationToken.None);
        await transaction.CommitAsync();
        return result;
    }

    private static async Task<int> StockOfAsync(ServiceProvider provider, Guid productId)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (await db.Products.AsNoTracking().SingleAsync(p => p.Id == productId)).Stock;
    }

    [Fact]
    public async Task Reserves_all_lines_when_stock_sufficient()
    {
        await using var provider = await CreateDatabaseAsync();
        var laptopBefore = await StockOfAsync(provider, SeedData.Laptop);
        var mouseBefore = await StockOfAsync(provider, SeedData.Mouse);

        var result = await ReserveAsync(provider, (SeedData.Laptop, 2), (SeedData.Mouse, 3));

        Assert.True(result.Reserved);
        Assert.Null(result.Reason);
        Assert.Equal(laptopBefore - 2, await StockOfAsync(provider, SeedData.Laptop));
        Assert.Equal(mouseBefore - 3, await StockOfAsync(provider, SeedData.Mouse));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(2, await db.StockReservations.CountAsync());
    }

    [Fact]
    public async Task Rejects_all_when_one_line_lacks_stock_and_changes_nothing()
    {
        await using var provider = await CreateDatabaseAsync();
        var laptopBefore = await StockOfAsync(provider, SeedData.Laptop);
        var monitorBefore = await StockOfAsync(provider, SeedData.Monitor);

        var result = await ReserveAsync(provider, (SeedData.Laptop, 1), (SeedData.Monitor, monitorBefore + 1));

        Assert.False(result.Reserved);
        Assert.Contains("stock", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(laptopBefore, await StockOfAsync(provider, SeedData.Laptop));
        Assert.Equal(monitorBefore, await StockOfAsync(provider, SeedData.Monitor));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await db.StockReservations.ToListAsync());
    }

    [Fact]
    public async Task Rejects_unknown_product_with_reason()
    {
        await using var provider = await CreateDatabaseAsync();
        var unknown = Guid.NewGuid();
        var laptopBefore = await StockOfAsync(provider, SeedData.Laptop);

        var result = await ReserveAsync(provider, (SeedData.Laptop, 1), (unknown, 1));

        Assert.False(result.Reserved);
        Assert.Contains(unknown.ToString(), result.Reason);
        Assert.Equal(laptopBefore, await StockOfAsync(provider, SeedData.Laptop));
    }

    [Fact]
    public async Task Sums_duplicate_product_lines_before_checking()
    {
        await using var provider = await CreateDatabaseAsync();
        var stock = await StockOfAsync(provider, SeedData.Monitor);
        Assert.True(stock >= 2, "seed data must leave the monitor with at least 2 units");
        var half = stock / 2 + 1;

        var tooMuch = await ReserveAsync(provider, (SeedData.Monitor, half), (SeedData.Monitor, half));
        var exact = await ReserveAsync(provider, (SeedData.Monitor, stock - 1), (SeedData.Monitor, 1));

        Assert.False(tooMuch.Reserved);
        Assert.True(exact.Reserved);
        Assert.Equal(0, await StockOfAsync(provider, SeedData.Monitor));
    }

    [Fact]
    public async Task Reserving_the_same_order_again_does_not_take_more_stock()
    {
        await using var provider = await CreateDatabaseAsync();
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        var first = await ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, 2));
        var second = await ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, 2));

        Assert.True(first.Reserved);
        Assert.True(second.Reserved); // the order already has its stock
        Assert.Equal(before - 2, await StockOfAsync(provider, SeedData.Laptop));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Equal(1, await db.StockReservations.CountAsync(r => r.OrderId == orderId));
    }

    [Fact]
    public async Task An_order_that_was_rejected_stays_rejected_when_it_is_evaluated_again()
    {
        await using var provider = await CreateDatabaseAsync();
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        var first = await ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, before + 1));
        // Another message for the same order, this time with a quantity that would fit.
        var second = await ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, 1));

        Assert.False(first.Reserved);
        Assert.False(second.Reserved);
        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(before, await StockOfAsync(provider, SeedData.Laptop));
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        Assert.Empty(await db.StockReservations.Where(r => r.OrderId == orderId).ToListAsync());
    }

    [Fact]
    public async Task Two_simultaneous_reservations_for_the_same_order_take_stock_once()
    {
        await using var provider = await CreateDatabaseAsync();
        var orderId = Guid.NewGuid();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        var results = await Task.WhenAll(
            ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, 2)),
            ReserveForOrderAsync(provider, orderId, (SeedData.Laptop, 2)));

        Assert.All(results, r => Assert.True(r.Reserved));
        Assert.Equal(before - 2, await StockOfAsync(provider, SeedData.Laptop));
    }

    [Fact]
    public async Task Rejection_reason_for_many_unknown_products_fits_the_orders_column()
    {
        await using var provider = await CreateDatabaseAsync();
        var lines = Enumerable.Range(0, 50).Select(_ => (Guid.NewGuid(), 1)).ToArray();

        var result = await ReserveAsync(provider, lines);

        Assert.False(result.Reserved);
        Assert.True(result.Reason!.Length <= 500, $"reason has {result.Reason.Length} characters");
        Assert.Contains("50", result.Reason); // says how many products were unknown
    }

    [Fact]
    public async Task Rejection_reason_for_many_products_without_stock_fits_the_orders_column()
    {
        await using var provider = await CreateDatabaseAsync();
        var empty = Enumerable.Range(0, 30).Select(i => new Product(Guid.NewGuid(), new string('x', 200), 0)).ToList();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.Products.AddRange(empty);
            await db.SaveChangesAsync();
        }

        var result = await ReserveAsync(provider, empty.Select(p => (p.Id, 1)).ToArray());

        Assert.False(result.Reserved);
        Assert.True(result.Reason!.Length <= 500, $"reason has {result.Reason.Length} characters");
        Assert.Contains("stock", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Quantities_that_overflow_when_summed_are_rejected_not_thrown()
    {
        await using var provider = await CreateDatabaseAsync();
        var before = await StockOfAsync(provider, SeedData.Laptop);

        var result = await ReserveAsync(provider, (SeedData.Laptop, int.MaxValue), (SeedData.Laptop, int.MaxValue));

        Assert.False(result.Reserved);
        Assert.Contains("stock", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await StockOfAsync(provider, SeedData.Laptop));
    }

    [Fact]
    public async Task Two_concurrent_orders_for_last_unit_reserve_only_one()
    {
        await using var provider = await CreateDatabaseAsync();
        var lastUnit = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.Products.Add(new Product(lastUnit, "Last unit", 1));
            await db.SaveChangesAsync();
        }

        var results = await Task.WhenAll(
            ReserveAsync(provider, (lastUnit, 1)),
            ReserveAsync(provider, (lastUnit, 1)));

        Assert.Single(results, r => r.Reserved);
        Assert.Single(results, r => !r.Reserved);
        Assert.Equal(0, await StockOfAsync(provider, lastUnit));
    }
}
