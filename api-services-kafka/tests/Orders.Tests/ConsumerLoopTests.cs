using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Contracts;
using Orders.Api.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Orders.Tests;

[Collection("kafka")]
public class ConsumerLoopTests(KafkaFixture kafka)
{
    /// <summary>Test double for the handler so the loop's own behavior can be observed.</summary>
    private sealed class ScriptedHandler(Func<MessageEnvelope<JsonElement>, int, Task> script) : IMessageHandler
    {
        private readonly Dictionary<Guid, int> _calls = new();
        private readonly List<Guid> _succeeded = [];
        private readonly object _gate = new();

        public int CallsFor(Guid messageId) { lock (_gate) return _calls.GetValueOrDefault(messageId); }
        public bool Succeeded(Guid messageId) { lock (_gate) return _succeeded.Contains(messageId); }

        public async Task HandleAsync(MessageEnvelope<JsonElement> message, CancellationToken ct)
        {
            int attempt;
            lock (_gate) { _calls[message.MessageId] = attempt = _calls.GetValueOrDefault(message.MessageId) + 1; }
            await script(message, attempt);
            lock (_gate) _succeeded.Add(message.MessageId);
        }
    }

    private sealed record Harness(string Topic, string GroupId, ConsumerLoop Loop, IProducer<string, string> Producer)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Loop.StopAsync(CancellationToken.None);
            Loop.Dispose();
            Producer.Dispose();
        }
    }

    private async Task<Harness> StartAsync(ScriptedHandler handler)
    {
        var topic = $"inventory.events.{Guid.NewGuid():N}";
        var groupId = $"orders-{Guid.NewGuid():N}";
        var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = kafka.BootstrapAddress, Acks = Acks.All }).Build();
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapAddress,
            GroupId = groupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();

        var loop = new ConsumerLoop(
            consumer, producer, handler,
            Options.Create(new ConsumerLoopOptions
            {
                Topic = topic,
                MaxAttempts = 3,
                RetryBaseDelay = TimeSpan.FromMilliseconds(10)
            }),
            NullLogger<ConsumerLoop>.Instance);

        // Create the topic first so the consumer joins with it already existing.
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = kafka.BootstrapAddress }).Build())
        {
            await admin.CreateTopicsAsync(
                [new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
        }

        await loop.StartAsync(CancellationToken.None);
        return new Harness(topic, groupId, loop, producer);
    }

    private static string Envelope(Guid messageId) =>
        JsonSerializer.Serialize(new MessageEnvelope<OrderCreated>(
            messageId, EventTypes.OrderCreated, DateTimeOffset.UtcNow,
            new OrderCreated(Guid.NewGuid(), [new OrderLine(Guid.NewGuid(), 1)])), ContractJson.Options);

    private static async Task<DeliveryResult<string, string>> SendAsync(Harness h, string key, string value) =>
        await h.Producer.ProduceAsync(h.Topic, new Message<string, string> { Key = key, Value = value });

    private static async Task EventuallyAsync(Func<bool> condition, string because, int seconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            await Task.Delay(100);
        }
        Assert.Fail($"Timed out waiting for: {because}");
    }

    private List<ConsumeResult<string, string>> ReadDeadLetters(string topic, int expected, int seconds = 20)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapAddress,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.Dlq(topic));

        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (found.Count < expected && DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
            }
            catch (ConsumeException ex) when (ex.Error.Code == ErrorCode.UnknownTopicOrPart)
            {
                // The DLQ topic only exists once something was dead-lettered: no topic means no messages.
                Thread.Sleep(200);
                continue;
            }

            if (result is not null) found.Add(result);
        }
        return found;
    }

    private Offset CommittedOffset(Harness h, TopicPartition partition)
    {
        using var probe = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapAddress,
            GroupId = h.GroupId
        }).Build();
        return probe.Committed([partition], TimeSpan.FromSeconds(10)).Single().Offset;
    }

    [Fact]
    public async Task Commits_offset_only_after_success()
    {
        // Run continuations asynchronously: otherwise SetResult() would run the consumer loop inline on this thread.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new ScriptedHandler((_, _) => gate.Task);
        await using var h = await StartAsync(handler);
        var id = Guid.NewGuid();

        try
        {
            var delivered = await SendAsync(h, "k", Envelope(id));
            await EventuallyAsync(() => handler.CallsFor(id) == 1, "handler to start");
            await Task.Delay(1500);
            var whileHandling = CommittedOffset(h, delivered.TopicPartition);
            gate.SetResult();
            await EventuallyAsync(() => CommittedOffset(h, delivered.TopicPartition) == delivered.Offset + 1,
                "offset to be committed after the handler finished");

            Assert.NotEqual(delivered.Offset + 1, whileHandling);
        }
        finally
        {
            // Never leave the handler blocked: StopAsync would wait for it forever.
            gate.TrySetResult();
        }
    }

    [Fact]
    public async Task Retries_transient_failure_then_succeeds()
    {
        var handler = new ScriptedHandler((_, attempt) =>
            attempt < 3 ? throw new InvalidOperationException("transient") : Task.CompletedTask);
        await using var h = await StartAsync(handler);
        var id = Guid.NewGuid();

        await SendAsync(h, "k", Envelope(id));

        await EventuallyAsync(() => handler.Succeeded(id), "handler to succeed on the third attempt");
        Assert.Equal(3, handler.CallsFor(id));
        Assert.Empty(ReadDeadLetters(h.Topic, expected: 1, seconds: 3));
    }

    [Fact]
    public async Task Sends_to_dlq_after_three_failures_and_continues_with_next_message()
    {
        var failing = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var handler = new ScriptedHandler((m, _) =>
            m.MessageId == failing ? throw new InvalidOperationException("always fails") : Task.CompletedTask);
        await using var h = await StartAsync(handler);

        await SendAsync(h, "same-order", Envelope(failing));
        await SendAsync(h, "same-order", Envelope(healthy));

        await EventuallyAsync(() => handler.Succeeded(healthy), "the next message to be processed");
        Assert.Equal(3, handler.CallsFor(failing));
        var dead = Assert.Single(ReadDeadLetters(h.Topic, expected: 1));
        Assert.Contains(failing.ToString(), dead.Message.Value, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("always fails", Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes("error")));
        Assert.Equal(h.Topic, Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes("original-topic")));
    }

    [Fact]
    public async Task Invalid_json_goes_to_dlq_without_retries()
    {
        var handler = new ScriptedHandler((_, _) => Task.CompletedTask);
        await using var h = await StartAsync(handler);
        var healthy = Guid.NewGuid();

        await SendAsync(h, "k", "this is not json");
        await SendAsync(h, "k", Envelope(healthy));

        await EventuallyAsync(() => handler.Succeeded(healthy), "the next message to be processed");
        var dead = Assert.Single(ReadDeadLetters(h.Topic, expected: 1));
        Assert.Equal("this is not json", dead.Message.Value);
    }

    [Fact]
    public async Task Message_without_message_id_goes_to_dlq()
    {
        var handler = new ScriptedHandler((_, _) => Task.CompletedTask);
        await using var h = await StartAsync(handler);
        var healthy = Guid.NewGuid();
        const string noMessageId = """{"type":"OrderCreated","occurredAt":"2026-11-21T10:00:00Z","payload":{}}""";

        await SendAsync(h, "k", noMessageId);
        await SendAsync(h, "k", Envelope(healthy));

        await EventuallyAsync(() => handler.Succeeded(healthy), "the next message to be processed");
        var dead = Assert.Single(ReadDeadLetters(h.Topic, expected: 1));
        Assert.Equal(noMessageId, dead.Message.Value);
    }

    [Fact]
    public async Task Poison_message_from_handler_goes_to_dlq_without_retries()
    {
        var poison = Guid.NewGuid();
        var handler = new ScriptedHandler((m, _) =>
            m.MessageId == poison ? throw new PoisonMessageException("bad payload") : Task.CompletedTask);
        await using var h = await StartAsync(handler);
        var healthy = Guid.NewGuid();

        await SendAsync(h, "k", Envelope(poison));
        await SendAsync(h, "k", Envelope(healthy));

        await EventuallyAsync(() => handler.Succeeded(healthy), "the next message to be processed");
        Assert.Equal(1, handler.CallsFor(poison));
        Assert.Single(ReadDeadLetters(h.Topic, expected: 1));
    }
}
