namespace Framework.Outbox;

/// <summary>
/// A pending side effect (e.g. an email) persisted in the SAME transaction as the domain change,
/// so it survives a crash between commit and the effect running. A background dispatcher polls
/// unprocessed rows, routes each to its <see cref="IOutboxHandler"/> by <see cref="Type"/>, and
/// stamps <see cref="ProcessedAt"/>; failures bump <see cref="Attempts"/> and push
/// <see cref="NextAttemptAt"/> out (backoff) until a cap.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }

    /// <summary>Logical message name the dispatcher routes on, e.g. "password-reset-email".</summary>
    public string Type { get; set; } = default!;

    /// <summary>JSON-serialized payload.</summary>
    public string Payload { get; set; } = default!;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null ⇒ still pending.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>Not eligible for dispatch before this time (retry backoff). Null ⇒ eligible now.</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
