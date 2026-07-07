using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

/// <summary>The page-hide beacon body (navigator.sendBeacon): dwell time + the dish ids that scrolled
/// into view. All fields optional — the endpoint is lenient (fire-and-forget).</summary>
public sealed record SessionBeacon(double? DurationSeconds, List<string>? Items);

// Public view-tracking beacons under /public/track (anonymous, best-effort). Every failure path still
// succeeds — a tracking error must never break a guest's menu page.
//   GET  /public/track/{slug}         — the 1x1-pixel view beacon (counts one view, deduped per hour)
//   POST /public/track/{slug}/session — dwell time + viewed dishes
public static class TrackingEndpoints
{
    private const string VisitorCookie = "mm_v";
    private static readonly string[] BotMarkers = ["bot", "crawl", "spider", "preview", "headless", "curl", "wget"];

    // A 1x1 transparent GIF — the beacon always answers with it (and 200) so a guest never sees an error.
    private static readonly byte[] Pixel =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0x00, 0x00, 0x00,
        0xff, 0xff, 0xff, 0x21, 0xf9, 0x04, 0x01, 0x00, 0x00, 0x00, 0x00, 0x2c, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3b,
    ];

    public static void MapTracking(this RouteGroupBuilder pub)
    {
        pub.MapGet("/track/{slug}", async (
                string slug, HttpContext http, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!IsBot(http))
            {
                try
                {
                    var rest = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
                    if (rest is not null)
                    {
                        var visitor = EnsureVisitor(http);
                        var lang = Localization.PickLanguage(
                            http.Request.Query["lang"].ToString(), http.Request.Headers.AcceptLanguage.ToString(),
                            rest.SupportedLanguages, rest.DefaultLanguage);
                        await ViewTracking.RecordViewAsync(db, rest, visitor, lang, clock.GetUtcNow(), ct);
                    }
                }
                catch { /* fire-and-forget: a tracking error never breaks the guest page */ }
            }
            http.Response.Headers.CacheControl = "no-store";
            return Results.File(Pixel, "image/gif");
        })
        .WithName("TrackView").WithSummary("Count one public menu view (pixel beacon).");

        pub.MapPost("/track/{slug}/session", async (
                string slug, SessionBeacon? body, HttpContext http, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            try
            {
                var rest = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
                if (rest is not null)
                {
                    var now = clock.GetUtcNow();
                    if (body?.DurationSeconds is > 0)
                        await ViewTracking.RecordSessionAsync(db, rest, body.DurationSeconds.Value, now, ct);

                    var visitor = http.Request.Cookies[VisitorCookie];
                    if (!string.IsNullOrEmpty(visitor) && body?.Items is { Count: > 0 })
                    {
                        var ids = body.Items
                            .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                            .Where(g => g is not null).Select(g => g!.Value).Distinct().Take(100).ToArray();
                        if (ids.Length > 0) await ViewTracking.RecordItemViewsAsync(db, rest, ids, visitor, now, ct);
                    }
                }
            }
            catch { /* fire-and-forget */ }
            return Results.NoContent();
        })
        .WithName("TrackSession").WithSummary("Record a guest session's dwell time + viewed dishes.");
    }

    private static bool IsBot(HttpContext http)
    {
        var ua = http.Request.Headers.UserAgent.ToString().ToLowerInvariant();
        return BotMarkers.Any(ua.Contains);
    }

    // A stable per-visitor id (httpOnly cookie), minted on the first view and reused thereafter.
    private static string EnsureVisitor(HttpContext http)
    {
        var existing = http.Request.Cookies[VisitorCookie];
        if (!string.IsNullOrEmpty(existing)) return existing;

        var id = Guid.NewGuid().ToString();
        http.Response.Cookies.Append(VisitorCookie, id, new CookieOptions
        {
            Path = "/", MaxAge = TimeSpan.FromDays(365), HttpOnly = true, SameSite = SameSiteMode.Lax,
        });
        return id;
    }
}
