using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Inventory.Tests;

/// <summary>Hosts Inventory.Api against a fresh, uniquely named database on the shared container.</summary>
public sealed class InventoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public InventoryApiFactory(string containerConnectionString)
    {
        _connectionString = new NpgsqlConnectionStringBuilder(containerConnectionString)
        {
            Database = $"inventory_{Guid.NewGuid():N}"
        }.ConnectionString;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Inventory", _connectionString);
        builder.UseSetting("Kafka:Enabled", "false");
    }
}
