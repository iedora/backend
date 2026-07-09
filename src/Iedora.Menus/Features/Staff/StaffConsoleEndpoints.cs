using Framework.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record StaffOverviewResponse(
    int Restaurants, int ActiveMenus, int Items, int ViewsToday, int Views30d,
    int QrBound, int QrUnbound, IReadOnlyList<StaffRestaurantRow> TopByViews);

public sealed record StaffDirectoryResponse(IReadOnlyList<StaffRestaurantRow> Restaurants);

public sealed record StaffAlertsResponse(
    IReadOnlyList<StaffRestaurantRow> StaleRestaurants, IReadOnlyList<StaffRestaurantRow> EmptyMenus, int UnboundQr);

public sealed record StaffRestaurantDetailResponse(
    StaffRestaurantRow Restaurant, IReadOnlyList<MenuSummary> Menus, IReadOnlyList<DailyPoint> Trend);

// The cross-tenant staff oversight console (reads) under /api/staff (platform admin). Pure reads over
// the menu DB — the provisioning/payments/user-CRM/audit surfaces depend on services not yet ported.
public static class StaffConsoleEndpoints
{
    public static void MapStaffConsole(this RouteGroupBuilder staff)
    {
        // GET /api/staff/overview — platform totals + the top 5 restaurants by 30-day views.
        staff.MapGet("/overview", async (MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var today = Timezones.UtcToday(clock.GetUtcNow());
            var since = StaffReads.Since30(today);

            // Order the source by the 30-day view subquery (EF can't sort by the projected alias),
            // then project the top 5.
            var topSource = db.Restaurants.AsNoTracking()
                .OrderByDescending(r => db.DailyViews.Where(d => d.RestaurantId == r.Id && d.Day >= since).Sum(d => (int?)d.Count) ?? 0)
                .ThenByDescending(r => r.CreatedAt).Take(5);
            var top = await StaffReads.ProjectAsync(topSource, db, since, ct);

            return TypedResults.Ok(new StaffOverviewResponse(
                await db.Restaurants.CountAsync(ct),
                await db.Menus.CountAsync(m => m.Active, ct),
                await db.Items.CountAsync(ct),
                await db.DailyViews.Where(d => d.Day == today).SumAsync(d => (int?)d.Count, ct) ?? 0,
                await db.DailyViews.Where(d => d.Day >= since).SumAsync(d => (int?)d.Count, ct) ?? 0,
                await db.QrCodes.CountAsync(q => q.BoundAt != null, ct),
                await db.QrCodes.CountAsync(q => q.BoundAt == null, ct),
                top));
        })
        .WithName("StaffOverview").WithSummary("Platform-wide oversight metrics + top restaurants (staff).");

        // GET /api/staff/directory?q= — searchable restaurant list (name/slug), newest first, capped.
        staff.MapGet("/directory", async (string? q, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var since = StaffReads.Since30(Timezones.UtcToday(clock.GetUtcNow()));

            var source = db.Restaurants.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var like = $"%{q.Trim()}%";
                source = source.Where(r => EF.Functions.ILike(r.Name, like) || EF.Functions.ILike(r.Slug, like));
            }

            var rows = await StaffReads.ProjectAsync(
                source.OrderByDescending(r => r.CreatedAt).Take(Paging.MaxLimit), db, since, ct);
            return TypedResults.Ok(new StaffDirectoryResponse(rows));
        })
        .WithName("StaffDirectory").WithSummary("Search the cross-tenant restaurant directory (staff).");

        // GET /api/staff/alerts — restaurants needing attention: stale (a week old, zero recent views),
        // empty (no dishes), plus the count of unbound QR stickers.
        staff.MapGet("/alerts", async (MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var now = clock.GetUtcNow();
            var since = StaffReads.Since30(Timezones.UtcToday(now));
            var weekAgo = now.AddDays(-7);
            const int alertLimit = 100; // most stale/empty restaurants surfaced per bucket

            var stale = await StaffReads.ProjectAsync(
                db.Restaurants.AsNoTracking().Where(r => r.CreatedAt < weekAgo
                    && !db.DailyViews.Any(d => d.RestaurantId == r.Id && d.Day >= since))
                    .OrderBy(r => r.CreatedAt).Take(alertLimit), db, since, ct);

            var empty = await StaffReads.ProjectAsync(
                db.Restaurants.AsNoTracking().Where(r => !db.Items.Any(i => i.RestaurantId == r.Id))
                    .OrderBy(r => r.CreatedAt).Take(alertLimit), db, since, ct);

            var unboundQr = await db.QrCodes.CountAsync(q => q.BoundAt == null, ct);
            return TypedResults.Ok(new StaffAlertsResponse(stale, empty, unboundQr));
        })
        .WithName("StaffAlerts").WithSummary("Restaurants needing attention + unbound QR count (staff).");

        // GET /api/staff/restaurants/{id} — one restaurant's row + menus-with-counts + a 14-day trend.
        staff.MapGet("/restaurants/{id:guid}",
            async Task<Results<Ok<StaffRestaurantDetailResponse>, NotFound>> (
                Guid id, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            // This restaurant's own timezone drives its "today" (also the existence check).
            var tz = await db.Restaurants.AsNoTracking().Where(r => r.Id == id).Select(r => r.TimeZone).FirstOrDefaultAsync(ct);
            if (tz is null) return TypedResults.NotFound();
            var today = Timezones.LocalDay(clock.GetUtcNow(), tz);
            var since = StaffReads.Since30(today);

            var row = (await StaffReads.ProjectAsync(
                db.Restaurants.AsNoTracking().Where(r => r.Id == id), db, since, ct)).FirstOrDefault();
            if (row is null) return TypedResults.NotFound();

            var menus = await MenuSummaries.ForRestaurantAsync(db, id, ct);

            // 14-day view trend (zero-filled, oldest first).
            const int trendDays = 13; // 14 points incl. today
            var counts = (await db.DailyViews.AsNoTracking()
                    .Where(d => d.RestaurantId == id && d.Day >= today.AddDays(-trendDays))
                    .GroupBy(d => d.Day).Select(g => new { Day = g.Key, Count = g.Sum(x => x.Count) })
                    .ToListAsync(ct))
                .ToDictionary(x => x.Day, x => x.Count);
            var trend = DailyPoints.ZeroFilled(today, trendDays, counts);

            return TypedResults.Ok(new StaffRestaurantDetailResponse(row, menus, trend));
        })
        .WithName("StaffRestaurantDetail").WithSummary("A restaurant's oversight detail: counts, menus, trend (staff).");
    }
}
