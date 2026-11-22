using Confluent.Kafka;
using Testcontainers.Kafka;

namespace Inventory.Tests;

public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder("confluentinc/cp-kafka:7.9.0").Build();

    public string BootstrapAddress => _container.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // The first idempotent producer makes a fresh broker create its transaction coordinator state, which
        // can take several seconds. Do it once here so individual tests are not timing-sensitive.
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = BootstrapAddress,
            EnableIdempotence = true,
            Acks = Acks.All
        }).Build();
        await producer.ProduceAsync("warmup", new Message<string, string> { Key = "k", Value = "v" });
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("kafka")]
public class KafkaCollection : ICollectionFixture<KafkaFixture>;

/// <summary>Collection for tests that need both a real PostgreSQL and a real Kafka.</summary>
[CollectionDefinition("infra")]
public class InfraCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<KafkaFixture>;
