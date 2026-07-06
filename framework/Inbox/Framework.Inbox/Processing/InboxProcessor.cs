using Microsoft.EntityFrameworkCore;

namespace Framework.Inbox;

/// <summary>
/// The idempotent-consumer primitive. A message consumer (in-process, a Postgres poller, or a
/// broker subscriber) calls <see cref="ProcessOnceAsync"/> per received message; the dedup row
/// insert and the handler run in ONE transaction. Duplicate ids are skipped; a handler that
/// throws rolls the whole thing back (no ledger row), so an at-least-once redelivery retries it.
/// </summary>
public sealed class InboxProcessor(DbContext db, IEnumerable<IInboxHandler> handlers, TimeProvider clock)
{
    private readonly Dictionary<string, IInboxHandler> _handlers = handlers.ToDictionary(h => h.Type);

    /// <summary>Process a received message exactly once. Returns true if handled now, false if it
    /// was a duplicate (already processed). Throws on an unregistered type or a handler failure.</summary>
    public async Task<bool> ProcessOnceAsync(Guid messageId, string type, string payload, CancellationToken ct)
    {
        if (!_handlers.TryGetValue(type, out var handler))
            throw new InvalidOperationException($"No IInboxHandler registered for type '{type}'.");

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Claim the message id. ON CONFLICT DO NOTHING → 0 rows means it's a redelivery.
            var inserted = await db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO inbox ("MessageId", "Type", "Payload", "ReceivedAt")
                VALUES ({messageId}, {type}, {payload}, {clock.GetUtcNow()})
                ON CONFLICT DO NOTHING
                """, ct);

            if (inserted == 0)
            {
                await tx.CommitAsync(ct);
                return false; // already processed — idempotent no-op
            }

            await handler.HandleAsync(payload, ct); // throws → rolls back the dedup row → retryable
            await tx.CommitAsync(ct);
            return true;
        });
    }
}
