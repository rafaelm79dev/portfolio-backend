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
    public async Task<ReservationResult> ReserveAsync(
        Guid orderId, IReadOnlyList<OrderLine> items, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("ReserveAsync must run inside a database transaction.");

        var wanted = items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));
        var ids = wanted.Keys.Order().ToArray();

        // Lock in a fixed order (by id) so concurrent orders cannot deadlock each other.
        var products = await db.Products
            .FromSqlInterpolated($"SELECT * FROM products WHERE id = ANY({ids}) ORDER BY id FOR UPDATE")
            .ToDictionaryAsync(p => p.Id, ct);

        var unknown = ids.Where(id => !products.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            return ReservationResult.Rejected($"Unknown product(s): {string.Join(", ", unknown)}");

        var short_ = ids
            .Where(id => products[id].Stock < wanted[id])
            .Select(id => $"{products[id].Name} (requested {wanted[id]}, available {products[id].Stock})")
            .ToList();
        if (short_.Count > 0)
            return ReservationResult.Rejected($"Insufficient stock for: {string.Join("; ", short_)}");

        var now = clock.GetUtcNow();
        foreach (var id in ids)
        {
            products[id].Reserve(wanted[id]);
            db.StockReservations.Add(new StockReservation(orderId, id, wanted[id], now));
        }

        await db.SaveChangesAsync(ct);
        return ReservationResult.Success();
    }
}
