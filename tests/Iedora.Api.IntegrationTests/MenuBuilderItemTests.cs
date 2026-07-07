using System.Net;
using System.Net.Http.Json;
using Iedora.Identity;
using Iedora.Menus;
using Iedora.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Api.IntegrationTests;

// Richer /tree wire that captures the item fields the builder-item tests assert.
public sealed record VarWire(string label, int priceCents);
public sealed record ItemFullWire(string id, string name, int priceCents, string currency, bool available, string[] tags, VarWire[] variants);
public sealed record CatFullWire(string id, string name, ItemFullWire[] items);
public sealed record MenuFullWire(string id, string name, CatFullWire[] categories);
public sealed record TreeFullWire(MenuFullWire[] menus);

[TestClass]
public sealed class MenuBuilderItemTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static async Task SeedRestaurant(Guid tenantId, string slug, string currency = "EUR")
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        db.Restaurants.Add(new Restaurant
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Name = "Tasca", Slug = slug,
            DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = currency,
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateFrom(string path, object body, string bearer)
    {
        var resp = await PostJson(path, body, bearer);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        return Guid.Parse((await resp.Content.ReadFromJsonAsync<CreatedIdWire>())!.id);
    }

    // Owner + restaurant + one menu + one category; returns (token, slug, categoryId).
    private async Task<(string token, string slug, Guid categoryId)> Scaffold(string email, string slug, string currency = "EUR")
    {
        var (owner, tenantId) = await CreateOwnerWithTenant(email, Pw);
        await SeedRestaurant(tenantId, slug, currency);
        var menuId = await CreateFrom($"/api/restaurants/{slug}/menus", new { name = "Lunch" }, owner.accessToken);
        var catId = await CreateFrom($"/api/restaurants/{slug}/menus/{menuId}/categories", new { name = "Starters" }, owner.accessToken);
        return (owner.accessToken, slug, catId);
    }

    private async Task<ItemFullWire[]> Items(string slug, string bearer)
    {
        var tree = (await (await Get($"/api/restaurants/{slug}/tree", bearer)).Content.ReadFromJsonAsync<TreeFullWire>())!;
        return tree.menus[0].categories[0].items;
    }

    [TestMethod]
    public async Task Creates_an_item_with_tags_and_variants()
    {
        var (token, slug, catId) = await Scaffold("owner@a.pt", "a");
        await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items", new
        {
            name = "Beer", priceCents = 500, tags = new[] { "cold" },
            variants = new[] { new { label = "Pint", priceCents = 500 }, new { label = "Half", priceCents = 300 } },
        }, token);

        var items = await Items(slug, token);
        Assert.HasCount(1, items);
        Assert.AreEqual("Beer", items[0].name);
        Assert.AreEqual(500, items[0].priceCents);
        CollectionAssert.AreEqual(new[] { "cold" }, items[0].tags);
        Assert.HasCount(2, items[0].variants);
        Assert.AreEqual("Pint", items[0].variants[0].label);
    }

    [TestMethod]
    public async Task Item_inherits_the_restaurants_default_currency()
    {
        var (token, slug, catId) = await Scaffold("owner@b.pt", "b", currency: "USD");
        await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items",
            new { name = "Soda", priceCents = 200 }, token); // no currency

        Assert.AreEqual("USD", (await Items(slug, token))[0].currency);
    }

    [TestMethod]
    public async Task Strips_a_trailing_dot_from_the_name()
    {
        var (token, slug, catId) = await Scaffold("owner@c.pt", "c");
        await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items",
            new { name = "Bacalhau à Brás.", priceCents = 1200 }, token);

        Assert.AreEqual("Bacalhau à Brás", (await Items(slug, token))[0].name);
    }

    [TestMethod]
    public async Task Updates_an_item()
    {
        var (token, slug, catId) = await Scaffold("owner@d.pt", "d");
        var itemId = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items",
            new { name = "Soup", priceCents = 500 }, token);

        var patch = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}/items/{itemId}",
            new { name = "Cold Soup", priceCents = 450, available = false }, token);
        Assert.AreEqual(HttpStatusCode.NoContent, patch.StatusCode);

        var item = (await Items(slug, token))[0];
        Assert.AreEqual("Cold Soup", item.name);
        Assert.AreEqual(450, item.priceCents);
        Assert.IsFalse(item.available);
    }

    [TestMethod]
    public async Task Update_leaves_variants_when_absent_but_replaces_when_present()
    {
        var (token, slug, catId) = await Scaffold("owner@e.pt", "e");
        var itemId = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items", new
        {
            name = "Wine", priceCents = 600,
            variants = new[] { new { label = "Glass", priceCents = 600 } },
        }, token);

        // PATCH without a variants field → stored variants left intact.
        await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}/items/{itemId}",
            new { name = "House Wine", priceCents = 650 }, token);
        Assert.HasCount(1, (await Items(slug, token))[0].variants);

        // PATCH with an empty variants array → cleared.
        await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}/items/{itemId}",
            new { name = "House Wine", priceCents = 650, variants = Array.Empty<object>() }, token);
        Assert.IsEmpty((await Items(slug, token))[0].variants);
    }

    [TestMethod]
    public async Task Deletes_an_item()
    {
        var (token, slug, catId) = await Scaffold("owner@f.pt", "f");
        var itemId = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items",
            new { name = "Soup", priceCents = 500 }, token);

        var del = await Delete($"/api/restaurants/{slug}/items/{itemId}", token);
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        Assert.IsEmpty(await Items(slug, token));
    }

    [TestMethod]
    public async Task Reorders_items()
    {
        var (token, slug, catId) = await Scaffold("owner@g.pt", "g");
        var a = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items", new { name = "A", priceCents = 1 }, token);
        var b = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items", new { name = "B", priceCents = 2 }, token);
        var c = await CreateFrom($"/api/restaurants/{slug}/categories/{catId}/items", new { name = "C", priceCents = 3 }, token);

        var reorder = await SendJson(HttpMethod.Put, $"/api/restaurants/{slug}/categories/{catId}/item-order",
            new { orderedIds = new[] { c, a, b } }, token);
        Assert.AreEqual(HttpStatusCode.NoContent, reorder.StatusCode);

        CollectionAssert.AreEqual(new[] { "C", "A", "B" }, (await Items(slug, token)).Select(i => i.name).ToArray());
    }

    [TestMethod]
    public async Task Negative_price_is_rejected()
    {
        var (token, slug, catId) = await Scaffold("owner@h.pt", "h");
        var resp = await PostJson($"/api/restaurants/{slug}/categories/{catId}/items",
            new { name = "Free?", priceCents = -1 }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Item_under_an_unknown_category_is_404()
    {
        var (token, slug, _) = await Scaffold("owner@i.pt", "i");
        var resp = await PostJson($"/api/restaurants/{slug}/categories/{Guid.NewGuid()}/items",
            new { name = "X", priceCents = 100 }, token);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
