namespace Orders.Api.Domain;

public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string Topic { get; private set; } = "";
    public string Key { get; private set; } = "";
    public string Type { get; private set; } = "";
    public string Payload { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? PublishedAt { get; private set; }

    private OutboxMessage() { }

    /// <summary>
    /// <paramref name="payloadJson"/> is the full message envelope. Pass the envelope's MessageId as
    /// <paramref name="id"/> so consumers dedupe on the same id the outbox row carries.
    /// </summary>
    public static OutboxMessage Create(
        string topic, string key, string type, string payloadJson, DateTimeOffset now, Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Topic = topic,
        Key = key,
        Type = type,
        Payload = payloadJson,
        CreatedAt = now
    };

    public void MarkPublished(DateTimeOffset now) => PublishedAt = now;
}

public class ProcessedMessage
{
    public Guid MessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    private ProcessedMessage() { }

    public ProcessedMessage(Guid messageId, DateTimeOffset processedAt)
    {
        MessageId = messageId;
        ProcessedAt = processedAt;
    }
}
