using System.Text.Json;
using Contracts;
using Inventory.Api.Data;
using Inventory.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Api.Messaging;

/// <summary>
/// Reserves stock for an OrderCreated event. Everything happens in one transaction: the stock change, the
/// answer in the outbox and the "already processed" marker. Either all of it is saved or none.
/// </summary>
public sealed class OrderCreatedHandler(IServiceScopeFactory scopes, TimeProvider clock) : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope<JsonElement> message, CancellationToken ct)
    {
        if (message.Type != EventTypes.OrderCreated) return; // not an event this service reacts to

        var order = ReadPayload(message);

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        if (await db.ProcessedMessages.AnyAsync(p => p.MessageId == message.MessageId, ct))
            return; // delivered twice: nothing to do

        var result = await scope.ServiceProvider.GetRequiredService<ReservationService>()
            .ReserveAsync(order.OrderId, order.Items, ct);

        var now = clock.GetUtcNow();
        var replyId = Guid.NewGuid();
        var (type, envelopeJson) = result.Reserved
            ? (EventTypes.StockReserved, Serialize(replyId, EventTypes.StockReserved, now, new StockReserved(order.OrderId)))
            : (EventTypes.StockRejected, Serialize(replyId, EventTypes.StockRejected, now,
                new StockRejected(order.OrderId, result.Reason!)));

        db.OutboxMessages.Add(OutboxMessage.Create(
            Topics.InventoryEvents, order.OrderId.ToString(), type, envelopeJson, now, replyId));
        db.ProcessedMessages.Add(new ProcessedMessage(message.MessageId, now));

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private static OrderCreated ReadPayload(MessageEnvelope<JsonElement> message)
    {
        try
        {
            var order = message.Payload.Deserialize<OrderCreated>(ContractJson.Options);
            if (order is null || order.OrderId == Guid.Empty || order.Items is null || order.Items.Count == 0
                || order.Items.Any(i => i.ProductId == Guid.Empty || i.Quantity <= 0))
                throw new PoisonMessageException("OrderCreated payload is incomplete or has invalid lines");
            return order;
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("OrderCreated payload cannot be read", ex);
        }
    }

    private static string Serialize<T>(Guid id, string type, DateTimeOffset now, T payload) =>
        JsonSerializer.Serialize(new MessageEnvelope<T>(id, type, now, payload), ContractJson.Options);
}
