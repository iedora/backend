namespace Framework.Commands;

/// <summary>The outbox payload for a command: the command id (so the handler can find + update the
/// tracking row) plus the caller's data.</summary>
public sealed record CommandEnvelope<T>(Guid CommandId, T Data);
