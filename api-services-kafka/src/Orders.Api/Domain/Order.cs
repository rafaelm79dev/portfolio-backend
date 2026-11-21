using Contracts;

namespace Orders.Api.Domain;

public enum OrderStatus
{
    Pending,
    Confirmed,
    Rejected
}

public class OrderItem
{
    public int Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    private OrderItem() { }

    public OrderItem(Guid productId, int quantity)
    {
        ProductId = productId;
        Quantity = quantity;
    }
}

public class Order
{
    private readonly List<OrderItem> _items = [];

    public Guid Id { get; private set; }
    public OrderStatus Status { get; private set; }
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items;

    private Order() { }

    public static Order Create(IReadOnlyList<OrderLine> items, DateTimeOffset now)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            Status = OrderStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        order._items.AddRange(items.Select(i => new OrderItem(i.ProductId, i.Quantity)));
        return order;
    }

    public bool Confirm(DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending) return false;
        Status = OrderStatus.Confirmed;
        UpdatedAt = now;
        return true;
    }

    public bool Reject(string reason, DateTimeOffset now)
    {
        if (Status != OrderStatus.Pending) return false;
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
        UpdatedAt = now;
        return true;
    }
}
