namespace Inventory.Api.Domain;

public class StockReservation
{
    public int Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private StockReservation() { }

    public StockReservation(Guid orderId, Guid productId, int quantity, DateTimeOffset createdAt)
    {
        OrderId = orderId;
        ProductId = productId;
        Quantity = quantity;
        CreatedAt = createdAt;
    }
}
