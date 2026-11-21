using Testcontainers.Kafka;

namespace Orders.Tests;

public sealed class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _container = new KafkaBuilder("confluentinc/cp-kafka:7.9.0").Build();

    public string BootstrapAddress => _container.GetBootstrapAddress();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>Collection for tests that need both a real PostgreSQL and a real Kafka.</summary>
[CollectionDefinition("infra")]
public class InfraCollection : ICollectionFixture<PostgresFixture>, ICollectionFixture<KafkaFixture>;
