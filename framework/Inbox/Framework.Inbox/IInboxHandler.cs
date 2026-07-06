namespace Framework.Inbox;

/// <summary>
/// Handles one inbox message type. Implementations are registered in DI; the processor routes a
/// received message to the handler whose <see cref="Type"/> matches. Throw to signal failure —
/// the surrounding transaction rolls back (including the dedup row), so the message is retryable.
/// </summary>
public interface IInboxHandler
{
    string Type { get; }

    Task HandleAsync(string payload, CancellationToken ct);
}
