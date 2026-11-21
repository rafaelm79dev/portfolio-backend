using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Features.Products;

public record ProductResponse(Guid Id, string Name, int Stock);

public static class GetProducts
{
    public static async Task<IResult> List(InventoryDbContext db, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Stock))
            .ToListAsync(ct);
        return Results.Ok(products);
    }

    public static async Task<IResult> ById(Guid id, InventoryDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new ProductResponse(p.Id, p.Name, p.Stock))
            .FirstOrDefaultAsync(ct);
        return product is null ? Results.NotFound() : Results.Ok(product);
    }
}
