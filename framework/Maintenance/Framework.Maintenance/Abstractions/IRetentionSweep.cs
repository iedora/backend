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
