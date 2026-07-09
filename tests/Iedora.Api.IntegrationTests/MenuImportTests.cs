using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Iedora.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record ImpVariantWire(string label, int priceCents);
public sealed record ImpItemWire(string name, Dictionary<string, string>? nameI18n, string? description, int? priceCents, string? currency, bool? available, string[]? tags, ImpVariantWire[]? variants);
public sealed record ImpCatWire(string name, ImpItemWire[]? items);
public sealed record ImpMenuWire(string name, ImpCatWire[]? categories);
public sealed record ImpDocWire(ImpMenuWire[] menus);

[TestClass]
public sealed class MenuImportTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    // Seed "tasca" (en; supports en+pt) with one menu → one category → three telling items:
    // a normal priced dish (i18n + tag), a priceless dish, and a hidden variant-priced dish.
    private static async Task<Guid> Seed()
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant { Id = restId, TenantId = Guid.NewGuid(), Name = "Tasca", Slug = "tasca", DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR" });
        db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
        db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
        db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Currency = "EUR", Position = 0, Available = true, Tags = ["veg"], NameI18n = new LocalizedText { ["pt"] = "Sopa" } });
        db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Market Fish", PriceCents = 0, Currency = "EUR", Position = 1, Available = true });
        db.Items.Add(new Item { Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Wine", PriceCents = 0, Currency = "EUR", Position = 2, Available = false, Variants = [new Variant("Glass", null, 300), new Variant("Bottle", null, 1500)] });
        await db.SaveChangesAsync();
        return restId;
    }

    private async Task<string> Admin(string email) => (await RegisterLoginAsAdmin(email, Pw)).accessToken;

    // Record `count` views against the seeded "Soup" dish; returns its (soon-to-be-replaced) item id.
    private static async Task<Guid> SeedSoupViews(Guid restId, int count)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var soup = await db.Items.Where(i => i.RestaurantId == restId && i.Name == "Soup").Select(i => i.Id).SingleAsync();
        var tenant = await db.Restaurants.Where(r => r.Id == restId).Select(r => r.TenantId).SingleAsync();
        db.ItemViews.Add(new ItemView { RestaurantId = restId, TenantId = tenant, ItemId = soup, Day = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime), Count = count });
        await db.SaveChangesAsync();
        return soup;
    }

    private static async Task<T> Query<T>(Func<MenuDbContext, Task<T>> q)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        return await q(scope.ServiceProvider.GetRequiredService<MenuDbContext>());
    }

    private static Task<int> ViewsOfDish(Guid restId, string name) => Query(async db =>
        await (from iv in db.ItemViews join i in db.Items on iv.ItemId equals i.Id
               where iv.RestaurantId == restId && i.Name == name select iv.Count).SumAsync());

    private static Task<int> AllItemViews(Guid restId) =>
        Query(async db => await db.ItemViews.Where(v => v.RestaurantId == restId).SumAsync(v => (int?)v.Count) ?? 0);

    private async Task<ImpDocWire> Export(Guid id, string token) =>
        (await (await Get($"/api/staff/restaurants/{id}/menus", token)).Content.ReadFromJsonAsync<ImpDocWire>())!;

    private async Task<HttpResponseMessage> Replace(Guid id, object body, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/staff/restaurants/{id}/menus") { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(req);
    }

    [TestMethod]
    public async Task Export_serializes_the_live_tree_into_the_import_shape()
    {
        var id = await Seed();
        var doc = await Export(id, await Admin("i1@s.pt"));

        Assert.HasCount(1, doc.menus);
        Assert.AreEqual("Lunch", doc.menus[0].name);
        var items = doc.menus[0].categories![0].items!;
        Assert.HasCount(3, items);

        var soup = items.Single(i => i.name == "Soup");
        Assert.AreEqual(500, soup.priceCents);
        CollectionAssert.AreEqual(new[] { "veg" }, soup.tags);
        Assert.AreEqual("Sopa", soup.nameI18n!["pt"]);
        Assert.IsNull(soup.available);            // visible → omitted
        Assert.IsNull(soup.variants);

        Assert.IsNull(items.Single(i => i.name == "Market Fish").priceCents); // priceless → omitted

        var wine = items.Single(i => i.name == "Wine");
        Assert.IsNull(wine.priceCents);           // variant-priced → no top-level price
        Assert.AreEqual(false, wine.available);   // hidden → available:false
        Assert.HasCount(2, wine.variants!);
        Assert.AreEqual(1500, wine.variants!.Single(v => v.label == "Bottle").priceCents);
    }

    [TestMethod]
    public async Task Export_then_replace_round_trips_the_structure()
    {
        var id = await Seed();
        var admin = await Admin("i2@s.pt");
        var before = await Export(id, admin);

        Assert.AreEqual(HttpStatusCode.NoContent, (await Replace(id, before, admin)).StatusCode);

        var after = await Export(id, admin);
        Assert.AreEqual(before.menus.Length, after.menus.Length);
        Assert.AreEqual("Lunch", after.menus[0].name);
        var items = after.menus[0].categories![0].items!;
        Assert.HasCount(3, items);
        Assert.AreEqual(500, items.Single(i => i.name == "Soup").priceCents);
        Assert.AreEqual("Sopa", items.Single(i => i.name == "Soup").nameI18n!["pt"]);
        Assert.HasCount(2, items.Single(i => i.name == "Wine").variants!);
    }

    [TestMethod]
    public async Task Replace_swaps_the_entire_tree()
    {
        var id = await Seed();
        var admin = await Admin("i3@s.pt");

        var newDoc = new { menus = new[] { new { name = "Dinner", categories = new[] { new { name = "Mains", items = new[] { new { name = "Steak", priceCents = 2000 } } } } } } };
        Assert.AreEqual(HttpStatusCode.NoContent, (await Replace(id, newDoc, admin)).StatusCode);

        var doc = await Export(id, admin);
        Assert.HasCount(1, doc.menus);
        Assert.AreEqual("Dinner", doc.menus[0].name); // old "Lunch" gone
        var items = doc.menus[0].categories![0].items!;
        Assert.HasCount(1, items);
        Assert.AreEqual("Steak", items[0].name);
        Assert.AreEqual(2000, items[0].priceCents);
    }

    [TestMethod]
    public async Task An_unsupported_translation_is_rejected_and_nothing_changes()
    {
        var id = await Seed();
        var admin = await Admin("i4@s.pt");

        // "fr" isn't in the restaurant's supported languages (en, pt).
        var bad = new { menus = new[] { new { name = "X", categories = new[] { new { name = "Y", items = new[] { new { name = "Z", nameI18n = new Dictionary<string, string> { ["fr"] = "Zed" } } } } } } } };
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Replace(id, bad, admin)).StatusCode);

        // The original tree is untouched (the replace never began writing).
        var doc = await Export(id, admin);
        Assert.AreEqual("Lunch", doc.menus[0].name);
        Assert.HasCount(3, doc.menus[0].categories![0].items!);
    }

    [TestMethod]
    public async Task Too_many_menus_is_rejected()
    {
        var id = await Seed();
        var many = new { menus = Enumerable.Range(0, 21).Select(i => new { name = $"M{i}" }).ToArray() };
        Assert.AreEqual(HttpStatusCode.BadRequest, (await Replace(id, many, await Admin("i5@s.pt"))).StatusCode);
    }

    [TestMethod]
    public async Task Unknown_restaurant_404s_on_export_and_replace()
    {
        var admin = await Admin("i6@s.pt");
        Assert.AreEqual(HttpStatusCode.NotFound, (await Get($"/api/staff/restaurants/{Guid.NewGuid()}/menus", admin)).StatusCode);
        var doc = new { menus = new[] { new { name = "M" } } };
        Assert.AreEqual(HttpStatusCode.NotFound, (await Replace(Guid.NewGuid(), doc, admin)).StatusCode);
    }

    [TestMethod]
    public async Task Import_is_admin_only()
    {
        var id = await Seed();
        var (owner, _) = await CreateOwnerWithTenant("owner@i.pt", Pw);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Get($"/api/staff/restaurants/{id}/menus", owner.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task Re_importing_preserves_a_surviving_dishs_view_history()
    {
        var id = await Seed();
        var oldSoup = await SeedSoupViews(id, 9);
        var admin = await Admin("imp-keep@s.pt");

        // Round-trip the live tree (still has "Soup") — a destructive replace with new item ids.
        Assert.AreEqual(HttpStatusCode.NoContent, (await Replace(id, await Export(id, admin), admin)).StatusCode);

        Assert.AreEqual(9, await ViewsOfDish(id, "Soup")); // history carried to the new Soup by name
        var newSoup = await Query(db => db.Items.Where(i => i.RestaurantId == id && i.Name == "Soup").Select(i => i.Id).SingleAsync());
        Assert.AreNotEqual(oldSoup, newSoup); // ...even though the item was recreated with a fresh id
    }

    [TestMethod]
    public async Task Re_importing_drops_the_history_of_a_removed_dish()
    {
        var id = await Seed();
        await SeedSoupViews(id, 9);
        var admin = await Admin("imp-drop@s.pt");

        // Replace with a tree that no longer contains "Soup".
        var newDoc = new { menus = new[] { new { name = "Dinner", categories = new[] { new { name = "Mains", items = new[] { new { name = "Steak", priceCents = 2000 } } } } } } };
        Assert.AreEqual(HttpStatusCode.NoContent, (await Replace(id, newDoc, admin)).StatusCode);

        Assert.AreEqual(0, await AllItemViews(id)); // Soup is gone → its history goes too; Steak had none
    }
}
