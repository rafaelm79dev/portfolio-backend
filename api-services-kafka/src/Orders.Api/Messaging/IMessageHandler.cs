using System.Text.Json;
using Contracts;

namespace Orders.Api.Messaging;

public interface IMessageHandler
{
    Task HandleAsync(MessageEnvelope<JsonElement> message, CancellationToken ct);
}

/// <summary>
/// Thrown by a handler when a message can never be processed (retrying will not help).
/// The consumer loop sends it straight to the dead-letter topic.
/// </summary>
public class PoisonMessageException(string message, Exception? inner = null) : Exception(message, inner);
