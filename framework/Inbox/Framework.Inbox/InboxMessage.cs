namespace Framework.Inbox;

/// <summary>
/// A processed-message ledger row. Its <see cref="MessageId"/> (producer-assigned) is the dedup
/// key: because the row is inserted and the handler runs in ONE transaction, the row's presence
/// means "already processed successfully" — a redelivery of the same id is a no-op.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Type { get; set; } = default!;
    public string Payload { get; set; } = default!;
    public DateTimeOffset ReceivedAt { get; set; }
}
