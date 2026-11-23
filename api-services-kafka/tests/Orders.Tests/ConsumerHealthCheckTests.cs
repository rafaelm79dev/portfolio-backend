using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orders.Api.Health;

namespace Orders.Tests;

public class ConsumerHealthCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeStatus(bool running, DateTimeOffset? lastActivity) : IConsumerStatus
    {
        public bool IsRunning => running;
        public DateTimeOffset? LastActivityUtc => lastActivity;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Task<HealthCheckResult> CheckAsync(bool running, DateTimeOffset? lastActivity) =>
        new ConsumerHealthCheck(new FakeStatus(running, lastActivity), new FixedClock(Now))
            .CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Healthy_when_the_consumer_is_running_and_was_active_recently()
    {
        var result = await CheckAsync(running: true, lastActivity: Now.AddSeconds(-5));

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_consumer_is_not_running()
    {
        var result = await CheckAsync(running: false, lastActivity: Now.AddSeconds(-1));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_consumer_has_been_silent_for_too_long()
    {
        var result = await CheckAsync(running: true, lastActivity: Now - ConsumerHealthCheck.MaxSilence - TimeSpan.FromSeconds(1));

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_consumer_never_reported_activity()
    {
        var result = await CheckAsync(running: true, lastActivity: null);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
