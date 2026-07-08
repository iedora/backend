using System.Net;
using System.Net.Http.Json;
using Iedora.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record StaffRowWire(string id, string tenantId, string name, string slug, int menus, int items, int views30d, string createdAt);
public sealed record StaffOverviewWire(int restaurants, int activeMenus, int items, int viewsToday, int views30d, int qrBound, int qrUnbound, StaffRowWire[] topByViews);
public sealed record StaffDirectoryWire(StaffRowWire[] restaurants);
public sealed record StaffAlertsWire(StaffRowWire[] staleRestaurants, StaffRowWire[] emptyMenus, int unboundQr);
public sealed record StaffMenuWire(string id, string name, bool active, int categoryCount, int dishCount);
public sealed record StaffTrendWire(string day, int count);
public sealed record StaffDetailWire(StaffRowWire restaurant, StaffMenuWire[] menus, StaffTrendWire[] trend);

[TestClass]
public sealed class MenuStaffConsoleTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    // Create a restaurant with N active + N inactive menus, `items` dishes under a category, an
    // optional CreatedAt, and daily-view rows given as (daysAgo, count).
    private static async Task<Guid> AddRestaurant(
        Guid tenant, string slug, int activeMenus = 1, int inactiveMenus = 0, int items = 0,
        DateTimeOffset? createdAt = null, params (int daysAgo, int count)[] views)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var restId = Guid.CreateVersion7();
        var rest = new Restaurant { Id = restId, TenantId = tenant, Name = slug, Slug = slug, DefaultLanguage = "en", SupportedLanguages = ["en"], DefaultCurrency = "EUR" };
        if (createdAt is { } c) rest.CreatedAt = c;
        db.Restaurants.Add(rest);

        var firstMenu = Guid.Empty;
        for (var i = 0; i < activeMenus; i++) { var m = Guid.CreateVersion7(); if (i == 0) firstMenu = m; db.Menus.Add(new Menu { Id = m, RestaurantId = restId, Name = $"M{i}", Position = i, Active = true }); }
        for (var i = 0; i < inactiveMenus; i++) db.Menus.Add(new Menu { Id = Guid.CreateVersion7(), RestaurantId = restId, Name = $"D{i}", Position = 100 + i, Active = false });

        if (items > 0)
        {
            if (firstMenu == Guid.Empty) { firstMenu = Guid.CreateVersion7(); db.Menus.Add(new Menu { Id = firstMenu, RestaurantId = restId, Name = "M", Position = 0, Active = true }); }
            var cat = Guid.CreateVersion7();
            db.Categories.Add(new Category { Id = cat, MenuId = firstMenu, RestaurantId = restId, Name = "Cat", Position = 0 });
            for (var i = 0; i < items; i++) db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = cat, RestaurantId = restId, Name = $"I{i}", PriceCents = 100, Position = i, Available = true });
        }

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        foreach (var (daysAgo, count) in views)
            db.DailyViews.Add(new DailyView { RestaurantId = restId, TenantId = tenant, Day = today.AddDays(-daysAgo), Language = "en", Count = count });

        await db.SaveChangesAsync();
        return restId;
    }

    private static async Task AddQr(string code, Guid? boundTo)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        db.QrCodes.Add(new QrCode { Code = code, RestaurantId = boundTo, BoundAt = boundTo is null ? null : DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private async Task<string> Admin(string email) => (await RegisterLoginAsAdmin(email, Pw)).accessToken;

    [TestMethod]
    public async Task Overview_totals_and_top_by_views()
    {
        await AddRestaurant(Guid.NewGuid(), "tasca", activeMenus: 1, inactiveMenus: 1, items: 2, views: [(0, 3), (5, 2)]);
        await AddRestaurant(Guid.NewGuid(), "bistro", activeMenus: 1, items: 0);

        var resp = await Get("/api/staff/overview", await Admin("a1@s.pt"));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var o = (await resp.Content.ReadFromJsonAsync<StaffOverviewWire>())!;

        Assert.AreEqual(2, o.restaurants);
        Assert.AreEqual(2, o.activeMenus);  // one active each; tasca's inactive isn't counted
        Assert.AreEqual(2, o.items);
        Assert.AreEqual(3, o.viewsToday);
        Assert.AreEqual(5, o.views30d);     // 3 today + 2 five days ago
        Assert.AreEqual(0, o.qrBound);
        Assert.AreEqual(0, o.qrUnbound);
        Assert.AreEqual("tasca", o.topByViews[0].slug); // most views first
        Assert.AreEqual(5, o.topByViews[0].views30d);
        Assert.HasCount(2, o.topByViews);
    }

    [TestMethod]
    public async Task Directory_searches_and_carries_counts()
    {
        await AddRestaurant(Guid.NewGuid(), "tasca", items: 2, views: [(0, 4)]);
        await AddRestaurant(Guid.NewGuid(), "bistro", items: 1);
        var admin = await Admin("a2@s.pt");

        var hit = (await (await Get("/api/staff/directory?q=tas", admin)).Content.ReadFromJsonAsync<StaffDirectoryWire>())!;
        Assert.HasCount(1, hit.restaurants);
        Assert.AreEqual("tasca", hit.restaurants[0].slug);
        Assert.AreEqual(2, hit.restaurants[0].items);
        Assert.AreEqual(4, hit.restaurants[0].views30d);

        var all = (await (await Get("/api/staff/directory", admin)).Content.ReadFromJsonAsync<StaffDirectoryWire>())!;
        Assert.HasCount(2, all.restaurants);
    }

    [TestMethod]
    public async Task Alerts_flag_stale_and_empty_restaurants()
    {
        var tenant = Guid.NewGuid();
        var stale = await AddRestaurant(tenant, "stale", items: 1, createdAt: DateTimeOffset.UtcNow.AddDays(-10)); // old, no views
        await AddRestaurant(tenant, "empty", activeMenus: 1, items: 0);            // recent, no dishes
        var healthy = await AddRestaurant(tenant, "healthy", items: 1, views: [(0, 1)]);
        await AddQr("free1", boundTo: null);
        await AddQr("used1", boundTo: healthy);

        var a = (await (await Get("/api/staff/alerts", await Admin("a3@s.pt"))).Content.ReadFromJsonAsync<StaffAlertsWire>())!;

        CollectionAssert.AreEquivalent(new[] { stale.ToString() }, a.staleRestaurants.Select(r => r.id).ToArray());
        Assert.IsTrue(a.emptyMenus.Any(r => r.slug == "empty"));
        Assert.IsFalse(a.emptyMenus.Any(r => r.slug is "healthy" or "stale")); // both have a dish
        Assert.AreEqual(1, a.unboundQr);
    }

    [TestMethod]
    public async Task Restaurant_detail_returns_row_menus_and_a_14_day_trend()
    {
        var id = await AddRestaurant(Guid.NewGuid(), "tasca", activeMenus: 1, items: 2, views: [(0, 3)]);

        var resp = await Get($"/api/staff/restaurants/{id}", await Admin("a4@s.pt"));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var d = (await resp.Content.ReadFromJsonAsync<StaffDetailWire>())!;

        Assert.AreEqual("tasca", d.restaurant.slug);
        Assert.AreEqual(2, d.restaurant.items);
        Assert.HasCount(1, d.menus);
        Assert.AreEqual(1, d.menus[0].categoryCount);
        Assert.AreEqual(2, d.menus[0].dishCount);
        Assert.HasCount(14, d.trend);
        Assert.AreEqual(3, d.trend[^1].count); // today is last, oldest first
    }

    [TestMethod]
    public async Task Detail_404s_for_an_unknown_restaurant()
    {
        var resp = await Get($"/api/staff/restaurants/{Guid.NewGuid()}", await Admin("a5@s.pt"));
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task The_console_is_admin_only()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@s.pt", Pw);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Get("/api/staff/overview", owner.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Get("/api/staff/overview")).StatusCode);
    }
}
