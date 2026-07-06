using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Outbox;

/// <summary>
/// Dispatches pending outbox messages: routes each to its <see cref="IOutboxHandler"/>, stamps
/// <c>ProcessedAt</c> on success, or bumps <c>Attempts</c> + backs off <c>NextAttemptAt</c> on
/// failure until a cap. DbContext-agnostic — resolves the consuming service's DbContext (registered
/// as <see cref="DbContext"/> by <c>AddOutbox&lt;TContext&gt;</c>). Called on a loop by
/// <see cref="OutboxBackgroundService"/> (and directly by tests).
/// </summary>
public sealed class OutboxProcessor(
    DbContext db, IEnumerable<IOutboxHandler> handlers, TimeProvider clock,
    IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
{
    private readonly OutboxOptions _opt = options.Value;
    private readonly Dictionary<string, IOutboxHandler> _handlers = handlers.ToDictionary(h => h.Type);

    /// <summary>Dispatch one batch of eligible messages. Returns how many were handled.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var batch = await db.Set<OutboxMessage>()
            .Where(m => m.ProcessedAt == null
                        && m.Attempts < _opt.MaxAttempts
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(_opt.BatchSize)
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
            await db.SaveChangesAsync(ct);
        }
        return handled;
    }
}
