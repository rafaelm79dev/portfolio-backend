using System.Net;
using System.Net.Http.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Api.Data;
using Orders.Api.Domain;
using Orders.Api.Features.Orders;

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
    public async Task Post_with_more_lines_than_allowed_returns_400()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var lines = Enumerable.Range(0, CreateOrder.MaxLines + 1).Select(_ => (Guid.NewGuid(), 1)).ToArray();

        var response = await client.PostAsJsonAsync("/orders", Body(lines));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_quantity_above_the_limit_returns_400()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", Body((Guid.NewGuid(), CreateOrder.MaxQuantityPerLine + 1)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_exactly_the_maximum_lines_and_quantity_is_accepted()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var lines = Enumerable.Range(0, CreateOrder.MaxLines)
            .Select(_ => (Guid.NewGuid(), CreateOrder.MaxQuantityPerLine)).ToArray();

        var response = await client.PostAsJsonAsync("/orders", Body(lines));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostWithKeyAsync(HttpClient client, object body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<int> CountOrdersAsync(OrdersApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await db.Orders.CountAsync();
    }

    [Fact]
    public async Task Post_twice_with_the_same_idempotency_key_creates_one_order()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var body = Body((Guid.NewGuid(), 1));

        var first = await PostWithKeyAsync(client, body, "retry-1");
        var second = await PostWithKeyAsync(client, body, "retry-1");

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<CreatedResponse>())!.OrderId;
        var secondId = (await second.Content.ReadFromJsonAsync<CreatedResponse>())!.OrderId;
        Assert.Equal(firstId, secondId);
        Assert.Equal(1, await CountOrdersAsync(factory));
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<OrdersDbContext>().OutboxMessages.CountAsync());
    }

    [Fact]
    public async Task Post_with_different_idempotency_keys_creates_two_orders()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var body = Body((Guid.NewGuid(), 1));

        await PostWithKeyAsync(client, body, "key-a");
        await PostWithKeyAsync(client, body, "key-b");

        Assert.Equal(2, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task Post_without_an_idempotency_key_always_creates_a_new_order()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var body = Body((Guid.NewGuid(), 1));

        await PostWithKeyAsync(client, body, null);
        await PostWithKeyAsync(client, body, null);

        Assert.Equal(2, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task Simultaneous_posts_with_the_same_idempotency_key_create_one_order()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();
        var body = Body((Guid.NewGuid(), 1));

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostWithKeyAsync(client, body, "same-key")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        var ids = new HashSet<Guid>();
        foreach (var r in responses) ids.Add((await r.Content.ReadFromJsonAsync<CreatedResponse>())!.OrderId);
        Assert.Single(ids);
        Assert.Equal(1, await CountOrdersAsync(factory));
    }

    [Fact]
    public async Task Post_with_a_too_long_idempotency_key_returns_400()
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await PostWithKeyAsync(client, Body((Guid.NewGuid(), 1)), new string('k', CreateOrder.MaxIdempotencyKeyLength + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountOrdersAsync(factory));
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

    [Theory]
    [InlineData("/health")]
    [InlineData("/ready")]
    public async Task Health_endpoints_answer_200_when_everything_is_up(string path)
    {
        await using var factory = new OrdersApiFactory(postgres.ConnectionString);
        var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
