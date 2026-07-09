using Framework.Maintenance;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Iedora.Menus;

// The two view-dedup marker tables (view_seen, item_view_seen) only gate counts inside their dedup
// window; once the window has passed a marker can never gate again, so it is pure dead weight. These
// retention sweeps prune the expired markers on the app worker's schedule. The counter tables
// (daily_view, item_view) are never touched — they are the analytics of record.

/// <summary>How long expired view-dedup markers are kept before the sweeper prunes them. Both windows
/// carry deliberate slack over the actual dedup granularity (an hour / a local day) so a still-active
/// marker is never removed early.</summary>
public sealed class DedupRetentionOptions
{
    public const string SectionName = "MenuDedupRetention";

    /// <summary>Keep <c>view_seen</c> markers this many hours (dedup window is one UTC hour; slack
    /// covers clock skew and long-running requests). Floored at 1.</summary>
    public int ViewSeenHours { get; set; } = 3;

    /// <summary>Keep <c>item_view_seen</c> markers for days at or after <c>utcToday - this</c> (dedup
    /// window is one <em>local</em> day, so ≥2 covers any timezone's offset from UTC). Floored at 1.</summary>
    public int ItemViewSeenDays { get; set; } = 2;
}

/// <summary>Prunes <c>view_seen</c> markers older than the retention window.</summary>
internal sealed class ViewSeenSweep(
    MenuDbContext db, TimeProvider clock, IOptions<DedupRetentionOptions> options) : IRetentionSweep
{
    public string Name => "menu.view_seen";

    public Task<int> SweepAsync(CancellationToken ct)
    {
        var cutoff = clock.GetUtcNow().AddHours(-Math.Max(1, options.Value.ViewSeenHours));
        return db.ViewSeen.Where(v => v.HourStart < cutoff).ExecuteDeleteAsync(ct);
    }
}

/// <summary>Prunes <c>item_view_seen</c> markers from days before the retention window.</summary>
internal sealed class ItemViewSeenSweep(
    MenuDbContext db, TimeProvider clock, IOptions<DedupRetentionOptions> options) : IRetentionSweep
{
    public string Name => "menu.item_view_seen";

    public Task<int> SweepAsync(CancellationToken ct)
    {
        var utcToday = Timezones.UtcToday(clock.GetUtcNow());
        var cutoff = utcToday.AddDays(-Math.Max(1, options.Value.ItemViewSeenDays));
        return db.ItemViewSeen.Where(v => v.Day < cutoff).ExecuteDeleteAsync(ct);
    }
}

public static class MenuMaintenance
{
    /// <summary>Register the Menu module's background maintenance onto a host (the app worker): the
    /// MenuDbContext plus the retention sweeper and its dedup-marker sweeps. Self-contained — the
    /// worker never references the API web project.</summary>
    public static IHostApplicationBuilder AddMenuMaintenance(this IHostApplicationBuilder builder)
    {
        builder.AddMenuDb();
        builder.Services.Configure<RetentionOptions>(builder.Configuration.GetSection(RetentionOptions.SectionName));
        builder.Services.Configure<DedupRetentionOptions>(builder.Configuration.GetSection(DedupRetentionOptions.SectionName));
        builder.Services.AddRetentionSweeper();
        builder.Services.AddRetentionSweep<ViewSeenSweep>();
        builder.Services.AddRetentionSweep<ItemViewSeenSweep>();
        return builder;
    }
}
