using Framework.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Framework.Inbox;

/// <summary>How long consumed-message ledger rows are kept before pruning. The window must outlast the
/// broker's worst-case redelivery horizon (dead-letter + retry): a redelivery arriving after its row
/// is pruned would be re-processed, so this defaults generously.</summary>
public sealed class InboxRetentionOptions
{
    public const string SectionName = "InboxRetention";

    /// <summary>Days to keep a processed inbox row. Floored at 1; keep it comfortably above the
    /// transport's maximum redelivery delay.</summary>
    public int RetentionDays { get; set; } = 14;
}

/// <summary>Prunes inbox ledger rows older than the retention window (dedup only matters while a
/// redelivery is still possible).</summary>
public sealed class InboxRetentionSweep<TContext>(
    TContext db, TimeProvider clock, IOptions<InboxRetentionOptions> options) : IRetentionSweep
    where TContext : DbContext
{
    public string Name => $"inbox:{typeof(TContext).Name}";

    public Task<int> SweepAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddDays(-Math.Max(1, options.Value.RetentionDays));
        return db.Set<InboxMessage>()
            .Where(m => m.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}

public static class InboxRetentionExtensions
{
    /// <summary>Prune this context's consumed inbox rows on the retention sweeper's schedule. Adds the
    /// sweeper host if it isn't already registered; bind <see cref="InboxRetentionOptions"/> (section
    /// "InboxRetention") to override the default window.</summary>
    public static IServiceCollection AddInboxRetention<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.AddRetentionSweeper();
        services.AddRetentionSweep<InboxRetentionSweep<TContext>>();
        return services;
    }
}
