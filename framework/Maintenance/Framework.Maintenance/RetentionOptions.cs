namespace Framework.Maintenance;

/// <summary>How often the retention sweeper runs. The per-table retention windows live with each
/// <see cref="IRetentionSweep"/>, not here.</summary>
public sealed class RetentionOptions
{
    /// <summary>Configuration section name to bind from.</summary>
    public const string SectionName = "Retention";

    /// <summary>Minutes between sweeps (floored at 1). Sweeps are cheap deletes, so hourly is plenty.</summary>
    public int IntervalMinutes { get; set; } = 60;
}
