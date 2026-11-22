using System.Net;
using System.Net.Sockets;
using Confluent.Kafka;
using Npgsql;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace EndToEnd.Tests;

/// <summary>
/// The whole system in one place: a real PostgreSQL, a real Kafka and both APIs running in this process.
/// Kafka is bound to a fixed host port so it keeps the same address when a test stops and restarts it.
/// </summary>
public sealed class StackFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly int _kafkaPort = FreePort();
    private KafkaContainer _kafka = null!;
    private OrdersApp _ordersApp = null!;
    private InventoryApp _inventoryApp = null!;

    public HttpClient Orders { get; private set; } = null!;
    public HttpClient Inventory { get; private set; } = null!;
    public string KafkaBootstrap => $"localhost:{_kafkaPort}";
    public string OrdersConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:7.9.0")
            .WithPortBinding(_kafkaPort, KafkaBuilder.KafkaPort)
            .Build();
        await Task.WhenAll(_postgres.StartAsync(), _kafka.StartAsync());
        await WarmUpKafkaAsync();

        OrdersConnectionString = WithDatabase("orders_e2e");
        _ordersApp = new OrdersApp(new AppSettings(OrdersConnectionString, KafkaBootstrap, "orders-e2e"));
        _inventoryApp = new InventoryApp(new AppSettings(WithDatabase("inventory_e2e"), KafkaBootstrap, "inventory-e2e"));
        Orders = _ordersApp.CreateClient();
        Inventory = _inventoryApp.CreateClient();
    }

    public async Task DisposeAsync()
    {
        await _ordersApp.DisposeAsync();
        await _inventoryApp.DisposeAsync();
        await _kafka.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public Task StopKafkaAsync() => _kafka.StopAsync();

    public Task StartKafkaAsync() => _kafka.StartAsync();

    public async Task<string> ReadOutboxPayloadAsync(string orderKey)
    {
        await using var connection = new NpgsqlConnection(OrdersConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT payload::text FROM outbox_messages WHERE key = @key ORDER BY created_at LIMIT 1", connection);
        command.Parameters.AddWithValue("key", orderKey);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private string WithDatabase(string name) =>
        new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;

    private async Task WarmUpKafkaAsync()
    {
        // The first idempotent producer makes a fresh broker prepare its transaction coordinator.
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = KafkaBootstrap,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
        await producer.ProduceAsync("warmup", new Message<string, string> { Key = "k", Value = "v" });
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

[CollectionDefinition("stack")]
public class StackCollection : ICollectionFixture<StackFixture>;
