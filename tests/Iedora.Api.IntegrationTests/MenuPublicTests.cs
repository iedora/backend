using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shape of GET /public/r/{slug} (camelCase).
public sealed record PublicVariantWire(string label, int priceCents);
public sealed record PublicItemWire(string id, string name, string? description, int priceCents,
    string currency, string? imageUrl, string[] tags, PublicVariantWire[] variants);
public sealed record PublicCategoryWire(string id, string name, string? description, PublicItemWire[] items);
public sealed record PublicMenuWire(string id, string name, string? description, PublicCategoryWire[] categories);
public sealed record PublicRestaurantWire(string name, string slug, string? description, string? logoUrl, string? bannerUrl);
public sealed record PublicPayloadWire(PublicRestaurantWire restaurant, PublicMenuWire[] menus,
    string defaultLanguage, string[] supportedLanguages, string currentLanguage);

[TestClass]
public sealed class MenuPublicTests : IntegrationTestBase
{
    // Seed a restaurant with one active menu → category → two items (one available, one hidden),
    // plus pt overrides on the localized fields. Returns the slug.
    private static async Task<string> SeedTascaAsync()
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();

        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();

        db.Restaurants.Add(new Restaurant
        {
            Id = restId, TenantId = Guid.NewGuid(), Name = "Tasca do Zé", Slug = "tasca-do-ze",
            Description = "A tavern", DescriptionI18n = new LocalizedText { ["pt"] = "Uma tasca" },
            DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR",
        });
        db.Menus.Add(new Menu
        {
            Id = menuId, RestaurantId = restId, Name = "Lunch",
            NameI18n = new LocalizedText { ["pt"] = "Almoço" }, Position = 0, Active = true,
        });
        // An inactive menu — must never render.
        db.Menus.Add(new Menu { Id = Guid.CreateVersion7(), RestaurantId = restId, Name = "Draft", Position = 1, Active = false });
        db.Categories.Add(new Category
        {
            Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters",
            NameI18n = new LocalizedText { ["pt"] = "Entradas" }, Position = 0,
        });
        db.Items.Add(new Item
        {
            Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Soup",
            NameI18n = new LocalizedText { ["pt"] = "Sopa" }, PriceCents = 500, Currency = "EUR",
            Position = 0, Available = true, Tags = ["veg"],
            Variants = [new Variant("Bowl", new LocalizedText { ["pt"] = "Tigela" }, 500)],
        });
        db.Items.Add(new Item
        {
            Id = Guid.CreateVersion7(), CategoryId = catId, RestaurantId = restId, Name = "Secret",
            PriceCents = 999, Position = 1, Available = false, // hidden from guests
        });
        await db.SaveChangesAsync();
        return "tasca-do-ze";
    }

    [TestMethod]
    public async Task Renders_the_menu_in_the_default_language()
    {
        var slug = await SeedTascaAsync();

        var resp = await Client.GetAsync($"/public/r/{slug}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<PublicPayloadWire>())!;

        Assert.AreEqual("Tasca do Zé", body.restaurant.name);
        Assert.AreEqual("A tavern", body.restaurant.description);
        Assert.AreEqual("en", body.currentLanguage);
        Assert.HasCount(1, body.menus); // the inactive menu is excluded
        Assert.AreEqual("Lunch", body.menus[0].name);
        Assert.AreEqual("Starters", body.menus[0].categories[0].name);

        var items = body.menus[0].categories[0].items;
        Assert.HasCount(1, items); // the unavailable item is excluded
        Assert.AreEqual("Soup", items[0].name);
        Assert.AreEqual(500, items[0].priceCents);
        Assert.AreEqual("Bowl", items[0].variants[0].label);
        CollectionAssert.AreEqual(new[] { "veg" }, items[0].tags);
    }

    [TestMethod]
    public async Task Localizes_to_an_explicit_lang_query()
    {
        var slug = await SeedTascaAsync();

        var resp = await Client.GetAsync($"/public/r/{slug}?lang=pt");
        var body = (await resp.Content.ReadFromJsonAsync<PublicPayloadWire>())!;

        Assert.AreEqual("pt", body.currentLanguage);
        Assert.AreEqual("Uma tasca", body.restaurant.description);
        Assert.AreEqual("Almoço", body.menus[0].name);
        Assert.AreEqual("Entradas", body.menus[0].categories[0].name);
        Assert.AreEqual("Sopa", body.menus[0].categories[0].items[0].name);
        Assert.AreEqual("Tigela", body.menus[0].categories[0].items[0].variants[0].label);
    }

    [TestMethod]
    public async Task Negotiates_language_from_the_accept_header()
    {
        var slug = await SeedTascaAsync();

        var req = new HttpRequestMessage(HttpMethod.Get, $"/public/r/{slug}");
        req.Headers.Add("Accept-Language", "pt-BR,pt;q=0.9,en;q=0.5");
        var resp = await Client.SendAsync(req);
        var body = (await resp.Content.ReadFromJsonAsync<PublicPayloadWire>())!;

        Assert.AreEqual("pt", body.currentLanguage); // "pt-BR" → "pt"
        Assert.AreEqual("Almoço", body.menus[0].name);
    }

    [TestMethod]
    public async Task Unknown_slug_returns_404()
    {
        var resp = await Client.GetAsync("/public/r/nope");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Public_render_needs_no_authentication()
    {
        var slug = await SeedTascaAsync();
        // No bearer token set on the client — the guest surface is anonymous.
        var resp = await Client.GetAsync($"/public/r/{slug}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }
}
