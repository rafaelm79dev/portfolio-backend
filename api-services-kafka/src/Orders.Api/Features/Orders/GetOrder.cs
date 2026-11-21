using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;

namespace Orders.Api.Features.Orders;

public record OrderItemResponse(Guid ProductId, int Quantity);

public record OrderResponse(
    Guid Id, string Status, string? RejectionReason, IReadOnlyList<OrderItemResponse> Items);

public static class GetOrder
{
    public static async Task<IResult> Handle(Guid id, OrdersDbContext db, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return Results.NotFound();

        return Results.Ok(new OrderResponse(
            order.Id,
            order.Status.ToString(),
            order.RejectionReason,
            order.Items.Select(i => new OrderItemResponse(i.ProductId, i.Quantity)).ToList()));
    }
}
