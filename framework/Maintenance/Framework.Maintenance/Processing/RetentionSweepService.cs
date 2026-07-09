using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Maintenance;

/// <summary>
/// Runs every registered <see cref="IRetentionSweep"/> on a fixed interval (from
/// <see cref="RetentionOptions"/>), once at startup and then each tick. Each sweep runs in its own
/// scope so it can inject a scoped store, and a slow or throwing sweep never blocks the others.
/// Deletes are idempotent WHERE-bounded statements, so running this on several worker replicas is
/// safe — each just races to remove the same stale rows.
/// </summary>
public sealed class RetentionSweepService(
    IServiceScopeFactory scopes, IOptions<RetentionOptions> options,
    TimeProvider clock, ILogger<RetentionSweepService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, options.Value.IntervalMinutes));
        using var timer = new PeriodicTimer(interval, clock);
        do
        {
            await SweepAllAsync(ct);
        }
        while (await WaitForNextTickAsync(timer, ct));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }

    internal async Task SweepAllAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        foreach (var sweep in scope.ServiceProvider.GetServices<IRetentionSweep>())
        {
            try
            {
                var removed = await sweep.SweepAsync(ct);
                RowsPruned.Add(removed, new KeyValuePair<string, object?>(SweepTag, sweep.Name));
                if (removed > 0)
                    logger.LogInformation("Retention sweep {Sweep} pruned {Rows} rows.", sweep.Name, removed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                // One sweep's failure must not abort the rest — log and carry on to the next.
                logger.LogError(ex, "Retention sweep {Sweep} failed.", sweep.Name);
            }
        }
    }

    // OTel-first: rows pruned per sweep, exported via the ServiceDefaults "Framework.*" meter wildcard.
    internal const string MeterName = "Framework.Maintenance";
    private const string SweepTag = "sweep";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> RowsPruned = Meter.CreateCounter<long>("retention.rows_pruned");
}
