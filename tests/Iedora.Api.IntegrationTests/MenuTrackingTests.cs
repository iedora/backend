using System.Net;
using System.Net.Http.Json;
using Iedora.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class MenuTrackingTests : IntegrationTestBase
{
    private static async Task<(Guid restId, Guid itemId)> Seed(string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant { Id = restId, TenantId = Guid.NewGuid(), Name = "Tasca", Slug = slug, DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR" });
        db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
        db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
        db.Items.Add(new Item { Id = itemId, CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Position = 0, Available = true });
        await db.SaveChangesAsync();
        return (restId, itemId);
    }

    private async Task<HttpResponseMessage> TrackView(string slug, string? visitor = null, string? userAgent = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/public/track/{slug}");
        if (visitor is not null) req.Headers.Add("Cookie", $"mm_v={visitor}");
        if (userAgent is not null) req.Headers.Add("User-Agent", userAgent);
        return await Client.SendAsync(req);
    }

    private async Task<HttpResponseMessage> PostSession(string slug, object body, string? visitor = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/public/track/{slug}/session") { Content = JsonContent.Create(body) };
        if (visitor is not null) req.Headers.Add("Cookie", $"mm_v={visitor}");
        return await Client.SendAsync(req);
    }

    private static string? VisitorFrom(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var c in cookies)
            if (c.StartsWith("mm_v=")) return c["mm_v=".Length..].Split(';')[0];
        return null;
    }

    private static async Task<T> Query<T>(Func<MenuDbContext, Task<T>> q)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        return await q(scope.ServiceProvider.GetRequiredService<MenuDbContext>());
    }

    private static Task<int> DailyViews(Guid restId) =>
        Query(async db => await db.DailyViews.Where(d => d.RestaurantId == restId).SumAsync(d => (int?)d.Count) ?? 0);

    [TestMethod]
    public async Task View_beacon_counts_a_view_and_returns_the_pixel()
    {
        var (restId, _) = await Seed("tasca");
        var resp = await TrackView("tasca");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.AreEqual("image/gif", resp.Content.Headers.ContentType!.MediaType);
        Assert.AreEqual(1, await DailyViews(restId));
    }

    [TestMethod]
    public async Task Same_visitor_is_deduped_within_the_hour()
    {
        var (restId, _) = await Seed("tasca");
        var first = await TrackView("tasca");
        var visitor = VisitorFrom(first)!; // the beacon minted a visitor cookie
        Assert.IsNotNull(visitor);

        await TrackView("tasca", visitor); // same visitor, same hour → no new count
        Assert.AreEqual(1, await DailyViews(restId));
    }

    [TestMethod]
    public async Task Distinct_visitors_each_count()
    {
        var (restId, _) = await Seed("tasca");
        await TrackView("tasca"); // new visitor
        await TrackView("tasca"); // another new visitor (no cookie sent back)
        Assert.AreEqual(2, await DailyViews(restId));
    }

    [TestMethod]
    public async Task Bots_are_not_counted()
    {
        var (restId, _) = await Seed("tasca");
        var resp = await TrackView("tasca", userAgent: "Mozilla/5.0 (compatible; Googlebot/2.1)");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode); // still serves the pixel
        Assert.AreEqual(0, await DailyViews(restId));
    }

    [TestMethod]
    public async Task Unknown_slug_still_serves_the_pixel()
    {
        var resp = await TrackView("nope");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task Session_beacon_records_a_clamped_duration()
    {
        var (restId, _) = await Seed("tasca");
        var resp = await PostSession("tasca", new { durationSeconds = 999999 }); // over the cap
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        var duration = await Query(db => db.MenuSessions.Where(s => s.RestaurantId == restId).Select(s => s.DurationSeconds).SingleAsync());
        Assert.AreEqual(3600, duration); // clamped
    }

    [TestMethod]
    public async Task Session_beacon_records_item_views_for_a_known_visitor()
    {
        var (restId, itemId) = await Seed("tasca");
        var visitor = VisitorFrom(await TrackView("tasca"))!; // establish a visitor cookie first

        var resp = await PostSession("tasca", new { durationSeconds = 30, items = new[] { itemId.ToString() } }, visitor);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        var itemCount = await Query(async db => await db.ItemViews.Where(v => v.ItemId == itemId).SumAsync(v => (int?)v.Count) ?? 0);
        Assert.AreEqual(1, itemCount);
    }

    [TestMethod]
    public async Task Item_views_need_a_visitor_cookie()
    {
        var (_, itemId) = await Seed("tasca");
        // No visitor cookie → item views are skipped (session duration still records).
        await PostSession("tasca", new { durationSeconds = 10, items = new[] { itemId.ToString() } });

        var itemCount = await Query(db => db.ItemViews.CountAsync());
        Assert.AreEqual(0, itemCount);
    }
}
