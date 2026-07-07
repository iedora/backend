using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shapes of the owner/staff restaurant reads.
public sealed record MenuSummaryWire(string id, string name, bool active, int position, int categoryCount, int dishCount);
public sealed record RestaurantDetailWire(string id, string tenantId, string name, string slug,
    string defaultLanguage, string[] supportedLanguages, string defaultCurrency);
public sealed record RestaurantOverviewWire(RestaurantDetailWire restaurant, MenuSummaryWire[] menus);
public sealed record TreeItemWire(string id, string name, bool available, string[] tags);
public sealed record TreeCategoryWire(string id, string name, TreeItemWire[] items);
public sealed record TreeMenuWire(string id, string name, bool active, TreeCategoryWire[] categories);
public sealed record MenuTreeWire(TreeMenuWire[] menus, string defaultLanguage, string[] supportedLanguages);

[TestClass]
public sealed class MenuRestaurantReadTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    // Seed a restaurant owned by tenantId, with one active menu (a category + an available and an
    // unavailable item) and one inactive menu. Returns the slug.
    private static async Task<string> SeedAsync(Guid tenantId, string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();

        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant { Id = restId, TenantId = tenantId, Name = "Tasca", Slug = slug, DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR" });
        db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
        db.Menus.Add(new Menu { Id = Guid.CreateVersion7(), RestaurantId = restId, Name = "Draft", Position = 1, Active = false });
        db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
        db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Position = 0, Available = true, Tags = ["veg"] });
        db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Secret", PriceCents = 900, Position = 1, Available = false });
        await db.SaveChangesAsync();
        return slug;
    }

    [TestMethod]
    public async Task Owner_sees_their_restaurant_with_menu_counts()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@tasca.pt", Pw);
        var slug = await SeedAsync(tenantId, "tasca");

        var resp = await Get($"/api/restaurants/{slug}", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<RestaurantOverviewWire>())!;

        Assert.AreEqual("Tasca", body.restaurant.name);
        Assert.AreEqual(tenantId.ToString(), body.restaurant.tenantId);
        Assert.HasCount(2, body.menus); // includes the inactive menu (this is the editor view)
        var lunch = body.menus.Single(m => m.name == "Lunch");
        Assert.AreEqual(1, lunch.categoryCount);
        Assert.AreEqual(2, lunch.dishCount); // counts the unavailable item too
    }

    [TestMethod]
    public async Task Owner_gets_the_raw_builder_tree_including_hidden_content()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@bistro.pt", Pw);
        var slug = await SeedAsync(tenantId, "bistro");

        var resp = await Get($"/api/restaurants/{slug}/tree", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<MenuTreeWire>())!;

        Assert.HasCount(2, body.menus);                          // inactive menu present
        Assert.IsTrue(body.menus.Any(m => !m.active));
        var lunch = body.menus.Single(m => m.name == "Lunch");
        Assert.HasCount(2, lunch.categories[0].items);           // unavailable item present
        Assert.IsTrue(lunch.categories[0].items.Any(i => !i.available));
        CollectionAssert.AreEqual(new[] { "en", "pt" }, body.supportedLanguages);
    }

    [TestMethod]
    public async Task A_different_tenant_cannot_see_the_restaurant()
    {
        var (_, ownerTenant) = await CreateOwnerWithTenant("owner@a.pt", Pw);
        var slug = await SeedAsync(ownerTenant, "a-tasca");

        var (stranger, _) = await CreateOwnerWithTenant("stranger@b.pt", Pw);
        var resp = await Get($"/api/restaurants/{slug}", stranger.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode); // foreign == missing (no enumeration)
    }

    [TestMethod]
    public async Task Staff_can_read_any_tenants_restaurant()
    {
        var (_, ownerTenant) = await CreateOwnerWithTenant("owner@c.pt", Pw);
        var slug = await SeedAsync(ownerTenant, "c-tasca");

        var admin = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        var resp = await Get($"/api/restaurants/{slug}", admin.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode); // staff cross-tenant
    }

    [TestMethod]
    public async Task Unknown_restaurant_is_404()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@d.pt", Pw);
        var resp = await Get("/api/restaurants/nope", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_is_unauthorized()
    {
        var resp = await Get("/api/restaurants/whatever");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
