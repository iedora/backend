namespace Framework.Outbox;

/// <summary>
/// Handles one outbox message type. Implementations are registered in DI; the dispatcher routes
/// a message to the handler whose <see cref="Type"/> matches <see cref="OutboxMessage.Type"/>.
/// Throw to signal failure (the dispatcher records the attempt and retries with backoff).
/// </summary>
public interface IOutboxHandler
{
    /// <summary>The <see cref="OutboxMessage.Type"/> this handler processes.</summary>
    string Type { get; }

    Task HandleAsync(string payload, CancellationToken ct);
}

public sealed class OutboxOptions
{
    public int PollSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 10;
}
