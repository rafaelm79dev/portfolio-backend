using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Api.Data;

namespace Orders.Api.Messaging;

/// <summary>
/// Reads unpublished rows from the outbox table and sends them to Kafka.
/// Delivery is at-least-once: if the process dies after Kafka acknowledged a message but before the
/// row is marked as published, the message is sent again. Consumers deduplicate by message id.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopes,
    IProducer<string, string> producer,
    TimeProvider clock,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batchSize = options.Value.BatchSize;
        var pending = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE published_at IS NULL
                ORDER BY created_at
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var published = 0;
        foreach (var row in pending)
        {
            try
            {
                await producer.ProduceAsync(row.Topic, new Message<string, string>
                {
                    Key = row.Key,
                    Value = row.Payload,
                    Headers = new Headers { { "type", Encoding.UTF8.GetBytes(row.Type) } }
                }, ct);
            }
            catch (ProduceException<string, string> ex)
            {
                // Stop at the first failure so messages keep their order; the row stays pending.
                logger.LogWarning(ex, "Could not publish outbox message {MessageId}; will retry", row.Id);
                break;
            }

            row.MarkPublished(clock.GetUtcNow());
            published++;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return published;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var published = 0;
            try
            {
                published = await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publishing cycle failed");
            }

            if (published < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
