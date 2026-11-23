using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Inventory.Api.Data;
using Inventory.Api.Health;

namespace Inventory.Tests;

[Collection("postgres")]
public class DatabaseHealthCheckTests(PostgresFixture postgres)
{
    private static async Task<HealthCheckResult> CheckAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql(connectionString).Options;
        using var db = new InventoryDbContext(options);
        return await new DatabaseHealthCheck<InventoryDbContext>(db).CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task Healthy_when_the_database_answers()
    {
        var result = await CheckAsync(postgres.ConnectionString);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_database_cannot_be_reached()
    {
        var result = await CheckAsync("Host=localhost;Port=1;Database=x;Username=x;Password=x;Timeout=2");

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
