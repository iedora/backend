using Microsoft.EntityFrameworkCore;

namespace Framework.Inbox;

/// <summary>
/// The idempotent-consumer primitive. A message consumer (in-process, a Postgres poller, or a
/// broker subscriber) calls <see cref="ProcessOnceAsync"/> per received message; the dedup row
/// insert and the handler run in ONE transaction. Duplicate ids are skipped; a handler that
/// throws rolls the whole thing back (no ledger row), so an at-least-once redelivery retries it.
/// </summary>
public sealed class InboxProcessor<TContext>(TContext db, IEnumerable<IInboxHandler> handlers, TimeProvider clock)
    where TContext : DbContext
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

            // Claim the message id. ON CONFLICT DO NOTHING → 0 rows means it's a redelivery. The
            // table is resolved (schema-qualified) from the model, so it works whatever schema the
            // consuming context maps it into; the values are still bound as parameters.
            var sql = "INSERT INTO " + InboxTable() + " (\"MessageId\", \"Type\", \"Payload\", \"ReceivedAt\") "
                    + "VALUES ({0}, {1}, {2}, {3}) ON CONFLICT DO NOTHING";
            var inserted = await db.Database.ExecuteSqlRawAsync(sql, [messageId, type, payload, clock.GetUtcNow()], ct);

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

    /// <summary>The inbox table as a quoted, schema-qualified identifier, read from the model so the
    /// raw claim targets whichever schema the consuming context maps it into.</summary>
    private string InboxTable()
    {
        var entity = db.Model.FindEntityType(typeof(InboxMessage))
            ?? throw new InvalidOperationException("InboxMessage is not mapped — call modelBuilder.MapInbox().");
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema() ?? db.Model.GetDefaultSchema();
        return schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
    }
}
