using Framework.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Outbox;

/// <summary>How long dispatched (processed) outbox rows are kept before pruning — once handled they
/// linger only as an audit trail.</summary>
public sealed class OutboxRetentionOptions
{
    public const string SectionName = "OutboxRetention";

    /// <summary>Days to keep a processed outbox row. Floored at 1.</summary>
    public int RetentionDays { get; set; } = 7;
}

/// <summary>Prunes dispatched outbox rows (<see cref="OutboxMessage.ProcessedAt"/> set) older than the
/// retention window. Pending and failed-but-retrying rows (ProcessedAt null) are never touched, so a
/// message still in flight is safe regardless of the window.</summary>
public sealed class OutboxRetentionSweep<TContext>(
    TContext db, TimeProvider clock, IOptions<OutboxRetentionOptions> options) : IRetentionSweep
    where TContext : DbContext
{
    public string Name => $"outbox:{typeof(TContext).Name}";

    public Task<int> SweepAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-Math.Max(1, options.Value.RetentionDays));
        return db.Set<OutboxMessage>()
            .Where(o => o.ProcessedAt != null && o.ProcessedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}

public static class OutboxRetentionExtensions
{
    /// <summary>Prune this context's processed outbox rows on the retention sweeper's schedule. Adds
    /// the sweeper host if it isn't already registered; bind <see cref="OutboxRetentionOptions"/>
    /// (section "OutboxRetention") to override the default window.</summary>
    public static IServiceCollection AddOutboxRetention<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddRetentionSweeper();
        services.AddRetentionSweep<OutboxRetentionSweep<TContext>>();
        return services;
    }
}
