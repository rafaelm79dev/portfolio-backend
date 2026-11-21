namespace Orders.Api.Messaging;

public class KafkaOptions
{
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";
    public int MessageTimeoutMs { get; set; } = 10_000;
}

public class OutboxOptions
{
    public int BatchSize { get; set; } = 50;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}
