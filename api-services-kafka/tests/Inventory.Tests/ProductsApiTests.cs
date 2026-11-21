using System.Net;
using System.Net.Http.Json;
using Inventory.Api.Domain;

namespace Inventory.Tests;

[Collection("postgres")]
public class ProductsApiTests(PostgresFixture postgres)
{
    private sealed record ProductResponse(Guid Id, string Name, int Stock);

    [Fact]
    public async Task Get_products_returns_seeded_stock()
    {
        await using var factory = new InventoryApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var products = await client.GetFromJsonAsync<List<ProductResponse>>("/products");

        Assert.NotNull(products);
        Assert.Equal(SeedData.Products.Count, products.Count);
        var laptop = Assert.Single(products, p => p.Id == SeedData.Laptop);
        Assert.True(laptop.Stock > 0);
    }

    [Fact]
    public async Task Get_product_by_id_returns_it_and_unknown_id_returns_404()
    {
        await using var factory = new InventoryApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var found = await client.GetFromJsonAsync<ProductResponse>($"/products/{SeedData.Mouse}");
        var missing = await client.GetAsync($"/products/{Guid.NewGuid()}");

        Assert.Equal(SeedData.Mouse, found!.Id);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
