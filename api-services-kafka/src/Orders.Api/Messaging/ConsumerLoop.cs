using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Contracts;
using Microsoft.Extensions.Options;
using Orders.Api.Health;

namespace Orders.Api.Messaging;

/// <summary>
/// Reads one topic and hands each message to an <see cref="IMessageHandler"/>.
/// The offset is committed only after the handler finished (or the message was dead-lettered), so a crash
/// in the middle means the message is delivered again: at-least-once. Handlers must be idempotent.
/// </summary>
public sealed class ConsumerLoop(
    IConsumer<string, string> consumer,
    IProducer<string, string> deadLetterProducer,
    IMessageHandler handler,
    IOptions<ConsumerLoopOptions> options,
    ILogger<ConsumerLoop> logger) : BackgroundService, IConsumerStatus
{
    private long _lastActivityTicks;
    private volatile bool _running;

    public bool IsRunning => _running;

    public DateTimeOffset? LastActivityUtc =>
        Interlocked.Read(ref _lastActivityTicks) is var ticks and not 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    private void Touch() => Interlocked.Exchange(ref _lastActivityTicks, DateTime.UtcNow.Ticks);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Consume() blocks; yield first so host startup is not held up.
        await Task.Yield();
        _running = true;
        Touch();
        consumer.Subscribe(options.Value.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    logger.LogWarning(ex, "Kafka consume error: {Reason}", ex.Error.Reason);
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                Touch(); // proof of life, even when there was nothing to read
                if (result is null || result.IsPartitionEOF) continue;
                await ProcessAsync(result, stoppingToken);
                Touch();
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        finally
        {
            _running = false;
            consumer.Close();
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        consumer.Dispose();
    }

    private async Task ProcessAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        if (!TryParse(result.Message.Value, out var message, out var parseError))
        {
            logger.LogWarning("Invalid message at {Position}: {Error}", result.TopicPartitionOffset, parseError);
            await DeadLetterAndCommitAsync(result, $"invalid message: {parseError}", ct);
            return;
        }

        var max = options.Value.MaxAttempts;
        string failure = "unknown error";
        for (var attempt = 1; attempt <= max; attempt++)
        {
            try
            {
                Touch(); // every attempt is progress: a slow or retried message is not a stuck loop
                await handler.HandleAsync(message, ct);
                consumer.Commit(result);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (PoisonMessageException ex)
            {
                failure = ex.Message;
                logger.LogWarning(ex, "Message {MessageId} can never be processed", message.MessageId);
                break;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                logger.LogWarning(ex, "Attempt {Attempt}/{Max} failed for message {MessageId}",
                    attempt, max, message.MessageId);
                if (attempt < max)
                    await Task.Delay(options.Value.RetryBaseDelay * Math.Pow(2, attempt - 1), ct);
            }
        }

        await DeadLetterAndCommitAsync(result, failure, ct);
    }

    private async Task DeadLetterAndCommitAsync(ConsumeResult<string, string> result, string error, CancellationToken ct)
    {
        var headers = new Headers();
        if (result.Message.Headers is { } original)
            foreach (var h in original) headers.Add(h.Key, h.GetValueBytes());
        headers.Add("error", Encoding.UTF8.GetBytes(error));
        headers.Add("original-topic", Encoding.UTF8.GetBytes(result.Topic));
        headers.Add("original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString()));

        try
        {
            await deadLetterProducer.ProduceAsync(Topics.Dlq(result.Topic), new Message<string, string>
            {
                Key = result.Message.Key,
                Value = result.Message.Value,
                Headers = headers
            }, ct);
        }
        catch (ProduceException<string, string> ex)
        {
            // Without a copy in the DLQ we must not move on: rewind so the message is tried again.
            logger.LogError(ex, "Could not dead-letter {Position}; will retry", result.TopicPartitionOffset);
            consumer.Seek(result.TopicPartitionOffset);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
            return;
        }

        consumer.Commit(result);
    }

    private static bool TryParse(string? value, out MessageEnvelope<JsonElement> message, out string error)
    {
        message = null!;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "empty message";
            return false;
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<MessageEnvelope<JsonElement>>(value, ContractJson.Options);
            if (envelope is null || envelope.MessageId == Guid.Empty)
            {
                error = "missing messageId";
                return false;
            }

            if (string.IsNullOrWhiteSpace(envelope.Type))
            {
                error = "missing type";
                return false;
            }

            message = envelope;
            error = "";
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
