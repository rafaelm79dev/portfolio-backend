using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orders.Api.Health;

namespace Orders.Tests;

[Collection("kafka")]
public class KafkaHealthCheckTests(KafkaFixture kafka)
{
    private static async Task<HealthCheckResult> CheckAsync(string bootstrapServers)
    {
        using var producer = new ProducerBuilder<string, string>(
            new ProducerConfig { BootstrapServers = bootstrapServers }).Build();
        return await new KafkaHealthCheck(producer).CheckHealthAsync(new HealthCheckContext());
    }

    [Fact]
    public async Task Healthy_when_the_broker_answers()
    {
        var result = await CheckAsync(kafka.BootstrapAddress);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Unhealthy_when_the_broker_cannot_be_reached()
    {
        var result = await CheckAsync("localhost:1");

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
