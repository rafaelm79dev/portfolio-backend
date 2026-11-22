extern alias OrdersApi;
extern alias InventoryApi;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EndToEnd.Tests;

/// <summary>Settings shared by both apps so they talk to the same Kafka and run their workers quickly.</summary>
public sealed record AppSettings(string DatabaseConnectionString, string KafkaBootstrap, string ConsumerGroup);

public sealed class OrdersApp(AppSettings settings) : WebApplicationFactory<OrdersApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Orders", settings.DatabaseConnectionString);
        builder.UseSetting("Kafka:Enabled", "true");
        builder.UseSetting("Kafka:BootstrapServers", settings.KafkaBootstrap);
        builder.UseSetting("Kafka:GroupId", settings.ConsumerGroup);
        builder.UseSetting("Outbox:PollInterval", "00:00:00.200");
    }
}

public sealed class InventoryApp(AppSettings settings) : WebApplicationFactory<InventoryApi::Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Inventory", settings.DatabaseConnectionString);
        builder.UseSetting("Kafka:Enabled", "true");
        builder.UseSetting("Kafka:BootstrapServers", settings.KafkaBootstrap);
        builder.UseSetting("Kafka:GroupId", settings.ConsumerGroup);
        builder.UseSetting("Outbox:PollInterval", "00:00:00.200");
    }
}
