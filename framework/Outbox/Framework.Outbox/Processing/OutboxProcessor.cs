using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Outbox;

/// <summary>
/// Dispatches pending outbox messages for one DbContext type: routes each to its
/// <see cref="IOutboxHandler"/> (by <c>Type</c>, resolved from the app-wide handler pool), stamps
/// <c>ProcessedAt</c> on success, or bumps <c>Attempts</c> + backs off <c>NextAttemptAt</c> on
/// failure. Claims a batch with <c>FOR UPDATE SKIP LOCKED</c> inside a transaction, so multiple
/// dispatcher replicas take disjoint rows. Generic over the DbContext, so ONE worker can host the
/// dispatchers for many services (<c>AddOutbox&lt;EachDbContext&gt;()</c>). Postgres-targeted.
/// </summary>
public sealed class OutboxProcessor<TContext>(
    TContext db, IEnumerable<IOutboxHandler> handlers, TimeProvider clock,
    IOptions<OutboxOptions> options, ILogger<OutboxProcessor<TContext>> logger)
    where TContext : DbContext
{
    private readonly OutboxOptions _opt = options.Value;
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Type);

    /// <summary>Claim + dispatch one batch of eligible messages. Returns how many were handled.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();

        // One retriable unit: the claim (holding row locks) + the mark-done commit together.
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Lock + claim eligible rows; other dispatchers SKIP LOCKED past them. The table is
            // resolved (schema-qualified) from the model, so it works whichever schema the
            // consuming context maps the outbox into. Values are still bound as parameters.
            var sql = $"SELECT * FROM {OutboxTable()}\n" + """
                WHERE "ProcessedAt" IS NULL
                  AND "Attempts" < {0}
                  AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {1})
                ORDER BY "CreatedAt"
                LIMIT {2}
                FOR UPDATE SKIP LOCKED
                """;
            var batch = await db.Set<OutboxMessage>()
                .FromSqlRaw(sql, _opt.MaxAttempts, now, _opt.BatchSize)
                .ToListAsync(ct);

            var handled = 0;
            foreach (var message in batch)
            {
                try
                {
                    if (!_handlers.TryGetValue(message.Type, out var handler))
                        throw new InvalidOperationException($"No IOutboxHandler registered for type '{message.Type}'.");

                    await handler.HandleAsync(message.Payload, ct);
                    message.ProcessedAt = clock.GetUtcNow();
                    handled++;
                }
                catch (Exception ex)
                {
                    message.Attempts++;
                    message.LastError = ex.Message;
                    // Linear-ish backoff, capped, so a flaky effect doesn't hot-loop.
                    message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(300, 5 * message.Attempts));
                    logger.LogWarning(ex, "Outbox {Id} ({Type}) failed (attempt {Attempts}).", message.Id, message.Type, message.Attempts);
                }
            }

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return handled;
        });
    }

    /// <summary>The outbox table as a quoted, schema-qualified identifier, read from the model so
    /// the raw claim query targets whichever schema the consuming context maps it into.</summary>
    private string OutboxTable()
    {
        var entity = db.Model.FindEntityType(typeof(OutboxMessage))
            ?? throw new InvalidOperationException("OutboxMessage is not mapped — call modelBuilder.MapOutbox().");
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema() ?? db.Model.GetDefaultSchema();
        return schema is null ? $"\"{table}\"" : $"\"{schema}\".\"{table}\"";
    }
}
