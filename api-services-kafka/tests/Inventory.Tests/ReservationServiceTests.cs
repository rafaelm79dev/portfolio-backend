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
    private static async Task<ReservationResult> ReserveAsync(
        ServiceProvider provider, params (Guid productId, int quantity)[] lines)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var result = await scope.ServiceProvider.GetRequiredService<ReservationService>().ReserveAsync(
            Guid.NewGuid(), lines.Select(l => new OrderLine(l.productId, l.quantity)).ToList(), CancellationToken.None);
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
