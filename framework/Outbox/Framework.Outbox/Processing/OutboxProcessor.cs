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

            // Lock + claim eligible rows; other dispatchers SKIP LOCKED past them.
            var batch = await db.Set<OutboxMessage>()
                .FromSql($"""
                    SELECT * FROM outbox
                    WHERE "ProcessedAt" IS NULL
                      AND "Attempts" < {_opt.MaxAttempts}
                      AND ("NextAttemptAt" IS NULL OR "NextAttemptAt" <= {now})
                    ORDER BY "CreatedAt"
                    LIMIT {_opt.BatchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
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
}
