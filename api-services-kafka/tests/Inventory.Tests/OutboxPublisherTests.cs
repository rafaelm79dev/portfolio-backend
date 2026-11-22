using System.Text;
using Confluent.Kafka;
using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Inventory.Api.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Inventory.Tests;

[Collection("infra")]
public class OutboxPublisherTests(PostgresFixture postgres, KafkaFixture kafka)
{
    private const string Unreachable = "localhost:1";

    private async Task<ServiceProvider> CreateDatabaseAsync()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
        {
            Database = $"inventory_{Guid.NewGuid():N}"
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();
        return provider;
    }

    private static (OutboxPublisher Publisher, IProducer<string, string> Producer) CreatePublisher(
        ServiceProvider provider, string bootstrapServers)
    {
        var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            // Short when the broker is meant to be unreachable; generous when it is real, so a loaded machine
            // does not turn a slow first acknowledgement into a false failure.
            MessageTimeoutMs = bootstrapServers == Unreachable ? 2000 : 15000
        }).Build();

        var publisher = new OutboxPublisher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            producer,
            TimeProvider.System,
            Options.Create(new OutboxOptions()),
            NullLogger<OutboxPublisher>.Instance);
        return (publisher, producer);
    }

    private static async Task<Guid> SeedAsync(ServiceProvider provider, string topic, string key, string type)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var row = OutboxMessage.Create(topic, key, type, """{"hello":"world"}""", DateTimeOffset.UtcNow);
        db.OutboxMessages.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private static async Task<OutboxMessage> LoadAsync(ServiceProvider provider, Guid id)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    private List<ConsumeResult<string, string>> ReadFromTopic(string topic, int expected, int seconds = 15)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapAddress,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(topic);

        var messages = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (messages.Count < expected && DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                Thread.Sleep(200);
                continue;
            }

            if (result is not null) messages.Add(result);
        }
        return messages;
    }

    [Fact]
    public async Task Publishes_pending_rows_and_marks_them_published()
    {
        await using var provider = await CreateDatabaseAsync();
        var topic = $"inventory.events.{Guid.NewGuid():N}";
        var key = Guid.NewGuid().ToString();
        var id = await SeedAsync(provider, topic, key, EventTypes.StockReserved);
        var (publisher, producer) = CreatePublisher(provider, kafka.BootstrapAddress);
        using var _ = producer;

        var published = await publisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(1, published);
        var message = Assert.Single(ReadFromTopic(topic, expected: 1)).Message;
        Assert.Equal(key, message.Key);
        Assert.Equal(EventTypes.StockReserved, Encoding.UTF8.GetString(message.Headers.GetLastBytes("type")));
        Assert.NotNull((await LoadAsync(provider, id)).PublishedAt);
    }

    [Fact]
    public async Task Does_not_publish_twice_when_called_again()
    {
        await using var provider = await CreateDatabaseAsync();
        var topic = $"inventory.events.{Guid.NewGuid():N}";
        await SeedAsync(provider, topic, Guid.NewGuid().ToString(), EventTypes.StockReserved);
        var (publisher, producer) = CreatePublisher(provider, kafka.BootstrapAddress);
        using var _ = producer;

        var first = await publisher.PublishPendingAsync(CancellationToken.None);
        var second = await publisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Single(ReadFromTopic(topic, expected: 2, seconds: 4));
    }

    [Fact]
    public async Task Keeps_row_pending_when_kafka_is_unavailable()
    {
        await using var provider = await CreateDatabaseAsync();
        var id = await SeedAsync(provider, "inventory.events.down", Guid.NewGuid().ToString(), EventTypes.StockReserved);
        var (publisher, producer) = CreatePublisher(provider, Unreachable);
        using var _ = producer;

        var published = await publisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Null((await LoadAsync(provider, id)).PublishedAt);
    }

    [Fact]
    public async Task Publishes_after_kafka_recovers()
    {
        await using var provider = await CreateDatabaseAsync();
        var topic = $"inventory.events.{Guid.NewGuid():N}";
        var id = await SeedAsync(provider, topic, Guid.NewGuid().ToString(), EventTypes.StockReserved);

        var (downPublisher, downProducer) = CreatePublisher(provider, Unreachable);
        using (downProducer)
        {
            Assert.Equal(0, await downPublisher.PublishPendingAsync(CancellationToken.None));
        }

        var (upPublisher, upProducer) = CreatePublisher(provider, kafka.BootstrapAddress);
        using var _ = upProducer;
        var published = await upPublisher.PublishPendingAsync(CancellationToken.None);

        Assert.Equal(1, published);
        Assert.Single(ReadFromTopic(topic, expected: 1));
        Assert.NotNull((await LoadAsync(provider, id)).PublishedAt);
    }
}
