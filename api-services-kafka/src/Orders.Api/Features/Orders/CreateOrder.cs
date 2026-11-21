using System.Text.Json;
using Contracts;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Features.Orders;

public record CreateOrderLine(Guid ProductId, int Quantity);

public record CreateOrderRequest(List<CreateOrderLine>? Items);

public record CreateOrderResponse(Guid OrderId);

public static class CreateOrder
{
    public static async Task<IResult> Handle(
        CreateOrderRequest request, OrdersDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.GetUtcNow();
        var lines = request.Items!.Select(i => new OrderLine(i.ProductId, i.Quantity)).ToList();
        var order = Order.Create(lines, now);

        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope<OrderCreated>(
            messageId, EventTypes.OrderCreated, now, new OrderCreated(order.Id, lines));

        db.Orders.Add(order);
        db.OutboxMessages.Add(OutboxMessage.Create(
            Topics.OrdersEvents,
            order.Id.ToString(),
            EventTypes.OrderCreated,
            JsonSerializer.Serialize(envelope, ContractJson.Options),
            now,
            messageId));
        await db.SaveChangesAsync(ct);

        return Results.Accepted($"/orders/{order.Id}", new CreateOrderResponse(order.Id));
    }

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0)
            errors["items"] = ["At least one item is required."];
        else if (request.Items.Any(i => i.ProductId == Guid.Empty || i.Quantity <= 0))
            errors["items"] = ["Every item needs a productId and a quantity greater than zero."];
        return errors;
    }
}
