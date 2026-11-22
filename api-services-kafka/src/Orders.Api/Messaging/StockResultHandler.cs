using System.Text.Json;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Data;
using Orders.Api.Domain;

namespace Orders.Api.Messaging;

/// <summary>
/// Applies Inventory's answer (StockReserved / StockRejected) to the order. The status change and the
/// "already processed" marker are saved in one transaction, so a message delivered twice has no second effect.
/// </summary>
public sealed class StockResultHandler(IServiceScopeFactory scopes, TimeProvider clock) : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<JsonElement> message, CancellationToken ct)
    {
        Guid orderId;
        string? rejectionReason = null;
        switch (message.Type)
        {
            case EventTypes.StockReserved:
                orderId = Read<StockReserved>(message).OrderId;
                break;
            case EventTypes.StockRejected:
                var rejected = Read<StockRejected>(message);
                orderId = rejected.OrderId;
                rejectionReason = rejected.Reason;
                break;
            default:
                return; // not an event this service reacts to
        }

        if (orderId == Guid.Empty)
            throw new PoisonMessageException($"{message.Type} payload has no orderId");

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (await db.ProcessedMessages.AnyAsync(p => p.MessageId == message.MessageId, ct))
            return; // delivered twice: nothing to do

        // The order is saved together with its outbox row before any event leaves, so it must exist.
        // If it does not, throw: the loop retries and then dead-letters the message.
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new InvalidOperationException($"Order {orderId} not found");

        var now = clock.GetUtcNow();
        if (message.Type == EventTypes.StockReserved)
            order.Confirm(now);
        else
            order.Reject(rejectionReason ?? "Rejected by inventory", now);

        db.ProcessedMessages.Add(new ProcessedMessage(message.MessageId, now));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static T Read<T>(MessageEnvelope<JsonElement> message) where T : class
    {
        try
        {
            return message.Payload.Deserialize<T>(ContractJson.Options)
                ?? throw new PoisonMessageException($"{message.Type} payload is empty");
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException($"{message.Type} payload cannot be read", ex);
        }
    }
}
