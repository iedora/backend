using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// Atomic dedup-gated view/item counters + raw session durations (ported from data/views.ts). The
// dedup insert wins at most once per bucket and only that winner drives the counter increment — one
// statement, idempotent under retries. Raw SQL because EF can't express INSERT…ON CONFLICT upserts.
// Buckets are native types (date / hour-truncated timestamptz), passed as typed params.
internal static class ViewTracking
{
    public static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime);

    // The UTC hour a view lands in — the dedup granularity for menu views.
    private static DateTimeOffset HourStart(DateTimeOffset t)
    {
        var u = t.UtcDateTime;
        return new DateTimeOffset(u.Year, u.Month, u.Day, u.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Count one menu view (deduped per visitor/restaurant/hour).</summary>
    public static Task RecordViewAsync(
        MenuDbContext db, Restaurant r, Guid visitorId, string language, DateTimeOffset now, CancellationToken ct)
    {
        var (hour, day) = (HourStart(now), Day(now));
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH won AS (
              INSERT INTO menu.view_seen ("VisitorId", "RestaurantId", "HourStart")
              VALUES ({visitorId}, {r.Id}, {hour}) ON CONFLICT DO NOTHING
              RETURNING 1)
            INSERT INTO menu.daily_view ("RestaurantId", "TenantId", "Day", "Language", "Count")
            SELECT {r.Id}, {r.TenantId}, {day}, {language}, 1 FROM won
            ON CONFLICT ("RestaurantId", "Day", "Language") DO UPDATE SET "Count" = menu.daily_view."Count" + 1
            """, ct);
    }

    /// <summary>Count a batch of item views (deduped per visitor/item/day, restaurant-scoped).</summary>
    public static Task RecordItemViewsAsync(
        MenuDbContext db, Restaurant r, Guid[] itemIds, Guid visitorId, DateTimeOffset now, CancellationToken ct)
    {
        var day = Day(now);
        return db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH owned AS (
              SELECT i."Id" AS item_id FROM menu.items i
              WHERE i."RestaurantId" = {r.Id} AND i."Id" = ANY({itemIds})),
            won AS (
              INSERT INTO menu.item_view_seen ("VisitorId", "ItemId", "Day")
              SELECT {visitorId}, owned.item_id, {day} FROM owned
              ON CONFLICT DO NOTHING
              RETURNING "ItemId")
            INSERT INTO menu.item_view ("RestaurantId", "TenantId", "ItemId", "Day", "Count")
            SELECT {r.Id}, {r.TenantId}, won."ItemId", {day}, 1 FROM won
            ON CONFLICT ("ItemId", "Day") DO UPDATE SET "Count" = menu.item_view."Count" + 1
            """, ct);
    }

    /// <summary>Store one guest session's dwell time (clamped 1..3600s).</summary>
    /// <summary>Store one guest session's dwell time (clamped 1..3600s); returns the clamped seconds
    /// so the caller can record the engagement metric without re-deriving the clamp.</summary>
    public static async Task<int> RecordSessionAsync(
        MenuDbContext db, Restaurant r, double durationSeconds, DateTimeOffset now, CancellationToken ct)
    {
        var clamped = (short)Math.Clamp((int)Math.Round(durationSeconds), 1, 3600);
        db.MenuSessions.Add(new MenuSession
        {
            Id = Guid.CreateVersion7(), RestaurantId = r.Id, TenantId = r.TenantId,
            Day = Day(now), DurationSeconds = clamped, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        return clamped;
    }
}
