using System.Text.Json;
using Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Features.Orders;

public record CreateOrderLine(Guid ProductId, int Quantity);

public record CreateOrderRequest(List<CreateOrderLine>? Items);

public record CreateOrderResponse(Guid OrderId);

public static class CreateOrder
{
    public const int MaxLines = 100;
    public const int MaxQuantityPerLine = 10_000;
    public const string IdempotencyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 200;

    public static async Task<IResult> Handle(
        CreateOrderRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        OrdersDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null;

        var errors = Validate(request, idempotencyKey);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // A client that did not see the 202 can retry with the same key and gets the same order back.
        if (idempotencyKey is not null)
        {
            var existing = await FindByKeyAsync(db, idempotencyKey, ct);
            if (existing is not null) return Accepted(existing.Value);
        }

        var now = clock.GetUtcNow();
        var lines = request.Items!.Select(i => new OrderLine(i.ProductId, i.Quantity)).ToList();
        var order = Order.Create(lines, now, idempotencyKey);

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

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null
            && ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another request with the same key was faster: answer with its order.
            db.ChangeTracker.Clear();
            var existing = await FindByKeyAsync(db, idempotencyKey, ct);
            if (existing is not null) return Accepted(existing.Value);
            throw;
        }

        return Accepted(order.Id);
    }

    private static IResult Accepted(Guid orderId) =>
        Results.Accepted($"/orders/{orderId}", new CreateOrderResponse(orderId));

    private static Task<Guid?> FindByKeyAsync(OrdersDbContext db, string key, CancellationToken ct) =>
        db.Orders.AsNoTracking()
            .Where(o => o.IdempotencyKey == key)
            .Select(o => (Guid?)o.Id)
            .FirstOrDefaultAsync(ct);

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request, string? idempotencyKey)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0)
            errors["items"] = ["At least one item is required."];
        else if (request.Items.Count > MaxLines)
            errors["items"] = [$"An order can have at most {MaxLines} lines."];
        else if (request.Items.Any(i => i.ProductId == Guid.Empty || i.Quantity <= 0 || i.Quantity > MaxQuantityPerLine))
            errors["items"] = [$"Every item needs a productId and a quantity from 1 to {MaxQuantityPerLine}."];

        if (idempotencyKey is { Length: > MaxIdempotencyKeyLength })
            errors[IdempotencyHeader] = [$"The key can have at most {MaxIdempotencyKeyLength} characters."];
        return errors;
    }
}
