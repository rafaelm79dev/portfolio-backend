namespace Contracts;

public static class Topics
{
    public const string OrdersEvents = "orders.events";
    public const string InventoryEvents = "inventory.events";

    public static string Dlq(string topic) => topic + ".dlq";
}

public static class EventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string StockReserved = "StockReserved";
    public const string StockRejected = "StockRejected";
}
