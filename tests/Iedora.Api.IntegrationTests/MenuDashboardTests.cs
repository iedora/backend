using System.Net;
using System.Net.Http.Json;
using Iedora.Menus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shapes of the owner dashboard reads (camelCase; `day` is an ISO date string).
public sealed record DailyPointWire(string day, int count);
public sealed record MenuStatsWire(int total, int active);
public sealed record DishStatsWire(int total, string? lastAddedAt);
public sealed record TopDishWire(string itemId, string itemName, int viewCount);
public sealed record AnalyticsWire(
    string range, int totalScans, int todayScans, DailyPointWire[] dailyBreakdown,
    MenuStatsWire menus, DishStatsWire dishes, string[] languages, TopDishWire[] topDishes, int? avgSessionSeconds);
public sealed record MonthlyWire(int count);

[TestClass]
public sealed class MenuDashboardTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";
    private static DateOnly Today => DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

    // Seed the tenant's content: one active + one inactive menu, a category, and one dish ("Soup").
    private static async Task<(Guid restId, Guid itemId)> SeedContent(Guid tenantId, string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant { Id = restId, TenantId = tenantId, Name = "Tasca", Slug = slug, DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR" });
        db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
        db.Menus.Add(new Menu { Id = Guid.CreateVersion7(), RestaurantId = restId, Name = "Draft", Position = 1, Active = false });
        db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
        db.Items.Add(new Item { Id = itemId, CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Position = 0, Available = true });
        await db.SaveChangesAsync();
        return (restId, itemId);
    }

    private static async Task WithDb(Func<MenuDbContext, Task> act)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        await act(scope.ServiceProvider.GetRequiredService<MenuDbContext>());
    }

    [TestMethod]
    public async Task Analytics_aggregates_scans_content_top_dishes_and_dwell()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@dash.pt", Pw);
        var (restId, itemId) = await SeedContent(tenantId, "tasca");
        await WithDb(async db =>
        {
            db.DailyViews.Add(new DailyView { RestaurantId = restId, TenantId = tenantId, Day = Today, Language = "en", Count = 3 });
            db.DailyViews.Add(new DailyView { RestaurantId = restId, TenantId = tenantId, Day = Today.AddDays(-1), Language = "en", Count = 2 });
            db.ItemViews.Add(new ItemView { RestaurantId = restId, TenantId = tenantId, ItemId = itemId, Day = Today, Count = 5 });
            db.MenuSessions.Add(new MenuSession { Id = Guid.CreateVersion7(), RestaurantId = restId, TenantId = tenantId, Day = Today, DurationSeconds = 10, CreatedAt = DateTimeOffset.UtcNow });
            db.MenuSessions.Add(new MenuSession { Id = Guid.CreateVersion7(), RestaurantId = restId, TenantId = tenantId, Day = Today, DurationSeconds = 20, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        });

        var resp = await Get("/api/dashboard/analytics?range=7d", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var a = (await resp.Content.ReadFromJsonAsync<AnalyticsWire>())!;

        Assert.AreEqual("7d", a.range);
        Assert.AreEqual(5, a.totalScans);   // 3 today + 2 yesterday
        Assert.AreEqual(3, a.todayScans);
        Assert.HasCount(7, a.dailyBreakdown);                       // zero-filled window
        Assert.AreEqual(Today.ToString("yyyy-MM-dd"), a.dailyBreakdown[^1].day); // oldest first → today last
        Assert.AreEqual(3, a.dailyBreakdown[^1].count);
        Assert.AreEqual(0, a.dailyBreakdown[0].count);
        Assert.AreEqual(2, a.menus.total);
        Assert.AreEqual(1, a.menus.active);
        Assert.AreEqual(1, a.dishes.total);
        Assert.IsNotNull(a.dishes.lastAddedAt);
        CollectionAssert.AreEqual(new[] { "en", "pt" }, a.languages); // registry order
        Assert.HasCount(1, a.topDishes);
        Assert.AreEqual(itemId.ToString(), a.topDishes[0].itemId);
        Assert.AreEqual("Soup", a.topDishes[0].itemName);
        Assert.AreEqual(5, a.topDishes[0].viewCount);
        Assert.AreEqual(15, a.avgSessionSeconds); // (10 + 20) / 2
    }

    [TestMethod]
    public async Task Analytics_of_an_empty_tenant_is_all_zeroes()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@empty.pt", Pw);
        await SeedContent(tenantId, "empty"); // content exists, but no views/sessions recorded

        var a = (await (await Get("/api/dashboard/analytics?range=30d", owner.accessToken))
            .Content.ReadFromJsonAsync<AnalyticsWire>())!;

        Assert.AreEqual(0, a.totalScans);
        Assert.HasCount(30, a.dailyBreakdown);
        Assert.IsTrue(a.dailyBreakdown.All(p => p.count == 0));
        Assert.HasCount(0, a.topDishes);
        Assert.IsNull(a.avgSessionSeconds); // no sessions → null, not 0
    }

    [TestMethod]
    public async Task One_tenants_scans_never_leak_into_anothers()
    {
        var (owner, mine) = await CreateOwnerWithTenant("owner@mine.pt", Pw);
        var (mineRest, _) = await SeedContent(mine, "mine");
        var otherTenant = Guid.NewGuid();
        var (otherRest, _) = await SeedContent(otherTenant, "theirs");
        await WithDb(async db =>
        {
            db.DailyViews.Add(new DailyView { RestaurantId = mineRest, TenantId = mine, Day = Today, Language = "en", Count = 4 });
            // Another tenant's restaurant racking up views must be invisible to this caller.
            db.DailyViews.Add(new DailyView { RestaurantId = otherRest, TenantId = otherTenant, Day = Today, Language = "en", Count = 99 });
            await db.SaveChangesAsync();
        });

        var a = (await (await Get("/api/dashboard/analytics?range=today", owner.accessToken))
            .Content.ReadFromJsonAsync<AnalyticsWire>())!;
        Assert.AreEqual(4, a.totalScans); // not 103
    }

    [TestMethod]
    public async Task Unknown_or_missing_range_is_rejected()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@range.pt", Pw);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Get("/api/dashboard/analytics?range=year", owner.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Get("/api/dashboard/analytics", owner.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task Monthly_views_sums_the_current_calendar_month()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@month.pt", Pw);
        var (restId, _) = await SeedContent(tenantId, "month");
        var monthStart = new DateOnly(Today.Year, Today.Month, 1);
        await WithDb(async db =>
        {
            db.DailyViews.Add(new DailyView { RestaurantId = restId, TenantId = tenantId, Day = monthStart, Language = "en", Count = 6 });
            db.DailyViews.Add(new DailyView { RestaurantId = restId, TenantId = tenantId, Day = monthStart.AddDays(-1), Language = "en", Count = 100 }); // last month
            await db.SaveChangesAsync();
        });

        var resp = await Get("/api/dashboard/views/month", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var m = (await resp.Content.ReadFromJsonAsync<MonthlyWire>())!;
        Assert.AreEqual(6, m.count); // the prior-month row is excluded
    }

    [TestMethod]
    public async Task Dashboard_requires_authentication()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Get("/api/dashboard/analytics?range=7d")).StatusCode);
    }
}
