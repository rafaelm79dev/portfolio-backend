namespace Contracts;

public record MessageEnvelope<T>(Guid MessageId, string Type, DateTimeOffset OccurredAt, T Payload);

public record OrderLine(Guid ProductId, int Quantity);

public record OrderCreated(Guid OrderId, IReadOnlyList<OrderLine> Items);

public record StockReserved(Guid OrderId);

public record StockRejected(Guid OrderId, string Reason);
