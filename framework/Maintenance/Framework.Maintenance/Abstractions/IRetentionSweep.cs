namespace Framework.Maintenance;

/// <summary>
/// One periodic delete of stale rows. An implementation injects its own store (e.g. a DbContext),
/// deletes the rows past its retention window, and returns how many it removed. The
/// <see cref="RetentionSweepService"/> resolves every registered sweep and runs it on a fixed
/// interval. Keep <see cref="SweepAsync"/> a single idempotent WHERE-bounded delete so concurrent
/// replicas just race to remove the same already-stale rows.
/// </summary>
public interface IRetentionSweep
{
    /// <summary>Stable, low-cardinality identifier for logs and the pruned-rows metric
    /// (e.g. <c>"menu.view_seen"</c>).</summary>
    string Name { get; }

    /// <summary>Delete the rows past this sweep's retention window; return the number removed.</summary>
    Task<int> SweepAsync(CancellationToken ct);
}

/// <summary>How often the retention sweeper runs. The per-table retention windows live with each
/// <see cref="IRetentionSweep"/>, not here.</summary>
public sealed class RetentionOptions
{
    /// <summary>Configuration section name to bind from.</summary>
    public const string SectionName = "Retention";

    /// <summary>Minutes between sweeps (floored at 1). Sweeps are cheap deletes, so hourly is plenty.</summary>
    public int IntervalMinutes { get; set; } = 60;
}
