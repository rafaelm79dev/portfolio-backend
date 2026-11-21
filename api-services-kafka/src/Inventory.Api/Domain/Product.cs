namespace Inventory.Api.Domain;

public class Product
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public int Stock { get; private set; }

    private Product() { }

    public Product(Guid id, string name, int stock)
    {
        Id = id;
        Name = name;
        Stock = stock;
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        if (quantity > Stock)
            throw new InvalidOperationException($"Cannot reserve {quantity} of {Name}: only {Stock} in stock.");
        Stock -= quantity;
    }
}

public static class SeedData
{
    public static readonly Guid Laptop = Guid.Parse("a1b2c3d4-0001-4000-8000-000000000001");
    public static readonly Guid Mouse = Guid.Parse("a1b2c3d4-0002-4000-8000-000000000002");
    public static readonly Guid Keyboard = Guid.Parse("a1b2c3d4-0003-4000-8000-000000000003");
    public static readonly Guid Monitor = Guid.Parse("a1b2c3d4-0004-4000-8000-000000000004");
    public static readonly Guid Headset = Guid.Parse("a1b2c3d4-0005-4000-8000-000000000005");

    public static readonly IReadOnlyList<Product> Products =
    [
        new(Laptop, "Laptop", 10),
        new(Mouse, "Mouse", 50),
        new(Keyboard, "Keyboard", 30),
        new(Monitor, "Monitor", 5),
        new(Headset, "Headset", 20)
    ];
}
