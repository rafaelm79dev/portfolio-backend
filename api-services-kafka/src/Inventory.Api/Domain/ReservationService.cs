using Contracts;
using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Domain;

public record ReservationResult(bool Reserved, string? Reason)
{
    public static ReservationResult Success() => new(true, null);
    public static ReservationResult Rejected(string reason) => new(false, reason);
}

/// <summary>
/// Reserves stock for every line of an order, or for none of them.
/// Must run inside a transaction: the product rows are locked with FOR UPDATE until it commits,
/// so two orders racing for the last unit are serialized and the second one sees the updated stock.
/// </summary>
public class ReservationService(InventoryDbContext db, TimeProvider clock)
{
    // The reason travels to orders, which stores at most 500 characters. Stay well below that.
    private const int MaxListed = 3;
    private const int MaxSummaryLength = 400;

    private static string Summarize(IReadOnlyList<string> items)
    {
        var text = string.Join("; ", items.Take(MaxListed));
        if (items.Count > MaxListed)
            text += $" and {items.Count - MaxListed} more ({items.Count} in total)";
        return text.Length > MaxSummaryLength ? text[..MaxSummaryLength] : text;
    }

    public async Task<ReservationResult> ReserveAsync(
        Guid orderId, IReadOnlyList<OrderLine> items, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("ReserveAsync must run inside a database transaction.");

        var wanted = items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Quantity)); // long: int sums can overflow
        var ids = wanted.Keys.Order().ToArray();

        // Lock in a fixed order (by id) so concurrent orders cannot deadlock each other.
        var products = await db.Products
            .FromSqlInterpolated($"SELECT * FROM products WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToDictionaryAsync(p => p.Id, ct);

        // Checked after the locks above, so two requests for the same order cannot both pass it.
        // A second OrderCreated for the same order (with another message id) must not take stock again.
        if (await db.StockReservations.AnyAsync(r => r.OrderId == orderId, ct))
            return ReservationResult.Success();

        var unknown = ids.Where(id => !products.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            return ReservationResult.Rejected(
                $"Unknown product(s): {Summarize(unknown.Select(id => id.ToString()).ToList())}");

        var short_ = ids
            .Where(id => products[id].Stock < wanted[id])
            .Select(id => $"{products[id].Name} (requested {wanted[id]}, available {products[id].Stock})")
            .ToList();
        if (short_.Count > 0)
            return ReservationResult.Rejected($"Insufficient stock for: {Summarize(short_)}");

        var now = clock.GetUtcNow();
        foreach (var id in ids)
        {
            // Safe: the stock check above guarantees the quantity is not larger than an int stock value.
            var quantity = (int)wanted[id];
            products[id].Reserve(quantity);
            db.StockReservations.Add(new StockReservation(orderId, id, quantity, now));
        }

        await db.SaveChangesAsync(ct);
        return ReservationResult.Success();
    }
}
