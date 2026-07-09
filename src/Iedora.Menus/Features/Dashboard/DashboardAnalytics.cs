using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// The owner dashboard's read model — aggregates the caller's tenant scans + content state over a
// range (ported from data/analytics.ts). All reads are over the tenant's restaurants; the analytics
// buckets are native `date`, so the range filter is an index-native comparison, not a string compare.

public sealed record MenuStats(int Total, int Active);
public sealed record DishStats(int Total, DateTimeOffset? LastAddedAt);
public sealed record TopDish(Guid ItemId, string ItemName, int ViewCount);

public sealed record AnalyticsResponse(
    string Range, int TotalScans, int TodayScans, IReadOnlyList<DailyPoint> DailyBreakdown,
    MenuStats Menus, DishStats Dishes, IReadOnlyList<string> Languages,
    IReadOnlyList<TopDish> TopDishes, int? AvgSessionSeconds);

internal static class DashboardAnalytics
{
    // The selectable ranges → how many days back the window spans (inclusive of today).
    private static readonly Dictionary<string, int> Ranges = new() { ["today"] = 0, ["7d"] = 6, ["30d"] = 29 };

    /// <summary>Resolve a range key to its span in days; false for an unknown/empty key.</summary>
    public static bool TryRange(string? key, out int days) => Ranges.TryGetValue(key ?? "", out days);

    // The zone the tenant's analytics days are bucketed in (all its restaurants share one; UTC if none).
    private static Task<string?> TenantZoneAsync(MenuDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Restaurants.AsNoTracking().Where(r => r.TenantId == tenantId).Select(r => r.TimeZone).FirstOrDefaultAsync(ct);

    /// <summary>Sum the tenant's menu views for the current calendar month (the tenant's local month).</summary>
    public static async Task<int> MonthlyViewsAsync(MenuDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var first = Timezones.LocalMonthStart(now, await TenantZoneAsync(db, tenantId, ct));
        return await db.DailyViews.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Day >= first)
            .SumAsync(d => (int?)d.Count, ct) ?? 0;
    }

    /// <summary>The full dashboard aggregate over <paramref name="days"/> back from today (UTC).</summary>
    public static async Task<AnalyticsResponse> BuildAsync(
        MenuDbContext db, Guid tenantId, string rangeKey, int days, DateTimeOffset now, CancellationToken ct)
    {
        var today = Timezones.LocalDay(now, await TenantZoneAsync(db, tenantId, ct));
        var start = today.AddDays(-days);

        // Scans per day over the window → total, today's, and a zero-filled breakdown (oldest first).
        var counts = (await db.DailyViews.AsNoTracking()
                .Where(d => d.TenantId == tenantId && d.Day >= start)
                .GroupBy(d => d.Day).Select(g => new { Day = g.Key, Count = g.Sum(x => x.Count) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Day, x => x.Count);

        var breakdown = DailyPoints.ZeroFilled(today, days, counts);

        // Content state: menu counts, dish count + last-added, across the tenant's restaurants.
        var menus = await (
            from m in db.Menus.AsNoTracking()
            join r in db.Restaurants.AsNoTracking() on m.RestaurantId equals r.Id
            where r.TenantId == tenantId
            group m by 1 into g
            select new MenuStats(g.Count(), g.Count(x => x.Active))).FirstOrDefaultAsync(ct) ?? new MenuStats(0, 0);

        var dishes = await (
            from i in db.Items.AsNoTracking()
            join r in db.Restaurants.AsNoTracking() on i.RestaurantId equals r.Id
            where r.TenantId == tenantId
            group i by 1 into g
            select new DishStats(g.Count(), (DateTimeOffset?)g.Max(x => x.CreatedAt))).FirstOrDefaultAsync(ct)
            ?? new DishStats(0, null);

        // Union of every restaurant's supported languages, registry-ordered.
        var supported = await db.Restaurants.AsNoTracking()
            .Where(r => r.TenantId == tenantId).Select(r => r.SupportedLanguages).ToListAsync(ct);
        var offered = supported.SelectMany(x => x).ToHashSet();
        var languages = Localization.Languages.Where(offered.Contains).ToList();

        // Top dishes — most-viewed items over the range, with the item's current name.
        var topDishes = await (
            from iv in db.ItemViews.AsNoTracking()
            join i in db.Items.AsNoTracking() on iv.ItemId equals i.Id
            where iv.TenantId == tenantId && iv.Day >= start
            group new { iv, i } by new { iv.ItemId, i.Name } into g
            orderby g.Sum(x => x.iv.Count) descending
            select new TopDish(g.Key.ItemId, g.Key.Name, g.Sum(x => x.iv.Count)))
            .Take(5).ToListAsync(ct);

        // Average guest dwell time over the range (whole seconds; null if no sessions).
        var avg = await db.MenuSessions.AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.Day >= start)
            .AverageAsync(s => (double?)s.DurationSeconds, ct);

        return new AnalyticsResponse(
            rangeKey, breakdown.Sum(p => p.Count), counts.GetValueOrDefault(today), breakdown,
            menus, dishes, languages, topDishes,
            avg is null ? null : (int)Math.Round(avg.Value));
    }
}
