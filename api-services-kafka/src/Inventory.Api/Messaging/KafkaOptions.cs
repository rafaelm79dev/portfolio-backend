namespace Inventory.Api.Messaging;

public class KafkaOptions
{
    public bool Enabled { get; set; } = true;
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string GroupId { get; set; } = "inventory-service";
    public int MessageTimeoutMs { get; set; } = 10_000;
}

public class ConsumerLoopOptions
{
    public string Topic { get; set; } = "";
    public int MaxAttempts { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);
}
