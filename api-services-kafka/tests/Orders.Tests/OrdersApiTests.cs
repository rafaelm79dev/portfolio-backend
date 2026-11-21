using System.Net;
using System.Net.Http.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Tests;

[Collection("postgres")]
public class OrdersApiTests(PostgresFixture postgres)
{
    private sealed record CreatedResponse(Guid OrderId);

    private sealed record OrderResponse(Guid Id, string Status, string? RejectionReason);

    private static object Body(params (Guid productId, int quantity)[] lines) =>
        new { items = lines.Select(l => new { productId = l.productId, quantity = l.quantity }) };

    [Fact]
    public async Task Post_order_persists_order_and_one_outbox_row_in_same_transaction()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", Body((Guid.NewGuid(), 2)));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<CreatedResponse>();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var order = await db.Orders.Include(o => o.Items).SingleAsync();
        var outbox = await db.OutboxMessages.SingleAsync();
        Assert.Equal(created!.OrderId, order.Id);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(Topics.OrdersEvents, outbox.Topic);
        Assert.Equal(order.Id.ToString(), outbox.Key);
        Assert.Equal(EventTypes.OrderCreated, outbox.Type);
        Assert.Null(outbox.PublishedAt);
    }

    [Fact]
    public async Task Post_with_empty_items_returns_400_and_writes_nothing()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", Body());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        Assert.Empty(await db.Orders.ToListAsync());
        Assert.Empty(await db.OutboxMessages.ToListAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Post_with_non_positive_quantity_returns_400(int quantity)
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", Body((Guid.NewGuid(), quantity)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_created_order_as_pending()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var created = await (await client.PostAsJsonAsync("/orders", Body((Guid.NewGuid(), 1))))
            .Content.ReadFromJsonAsync<CreatedResponse>();

        var order = await client.GetFromJsonAsync<OrderResponse>($"/orders/{created!.OrderId}");

        Assert.Equal(created.OrderId, order!.Id);
        Assert.Equal("Pending", order.Status);
    }

    [Fact]
    public async Task Get_unknown_order_returns_404()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/orders/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
