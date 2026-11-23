namespace Inventory.Api.Domain;

/// <summary>
/// What inventory decided for an order, kept forever. It makes the decision final: a later message for the
/// same order is answered with the same result instead of being evaluated again.
/// </summary>
public class OrderDecision
{
    public Guid OrderId { get; private set; }
    public bool Reserved { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset DecidedAt { get; private set; }

    private OrderDecision() { }

    public static OrderDecision ForReserved(Guid orderId, DateTimeOffset now) =>
        new() { OrderId = orderId, Reserved = true, DecidedAt = now };

    public static OrderDecision ForRejected(Guid orderId, string reason, DateTimeOffset now) =>
        new() { OrderId = orderId, Reserved = false, Reason = reason, DecidedAt = now };
}
