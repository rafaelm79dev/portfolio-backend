using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace Orders.Tests;

/// <summary>Hosts Orders.Api against a fresh, uniquely named database on the shared container.</summary>
public sealed class OrdersApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public OrdersApiFactory(string containerConnectionString)
    {
        var builder = new NpgsqlConnectionStringBuilder(containerConnectionString)
        {
            Database = $"orders_{Guid.NewGuid():N}"
        };
        _connectionString = builder.ConnectionString;
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Orders", _connectionString);
        builder.UseSetting("Kafka:Enabled", "false");
    }
}
