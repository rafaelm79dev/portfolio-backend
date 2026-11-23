using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.Api.Health;

/// <summary>What the consumer loop tells the outside world about itself.</summary>
public interface IConsumerStatus
{
    bool IsRunning { get; }

    /// <summary>Last time the loop was alive (it reports even when there was nothing to read).</summary>
    DateTimeOffset? LastActivityUtc { get; }
}

/// <summary>Unhealthy when the consumer stopped, or got stuck: a process can be alive with a dead consumer.</summary>
public sealed class ConsumerHealthCheck(IConsumerStatus status, TimeProvider clock) : IHealthCheck
{
    public static readonly TimeSpan MaxSilence = TimeSpan.FromSeconds(30);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!status.IsRunning)
            return Task.FromResult(HealthCheckResult.Unhealthy("The consumer is not running."));
        if (status.LastActivityUtc is not { } last)
            return Task.FromResult(HealthCheckResult.Unhealthy("The consumer has not reported any activity."));

        var silence = clock.GetUtcNow() - last;
        return Task.FromResult(silence > MaxSilence
            ? HealthCheckResult.Unhealthy($"The consumer has been silent for {silence.TotalSeconds:F0} seconds.")
            : HealthCheckResult.Healthy());
    }
}

/// <summary>Asks the broker for its metadata, using the connection the producer already has.</summary>
public sealed class KafkaHealthCheck(IProducer<string, string> producer) : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                using var admin = new DependentAdminClientBuilder(producer.Handle).Build();
                admin.GetMetadata(Timeout);
                return HealthCheckResult.Healthy();
            }
            catch (KafkaException ex)
            {
                return HealthCheckResult.Unhealthy("Kafka does not answer.", ex);
            }
        }, ct);
}

public sealed class DatabaseHealthCheck<TContext>(TContext db) : IHealthCheck where TContext : DbContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            return await db.Database.CanConnectAsync(timeout.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("The database does not answer.");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("The database check failed.", ex);
        }
    }
}
