using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record ThemeWire(string? primaryColor, string? secondaryColor, string? font, string? layout);
public sealed record RestDetailWire(string id, string name, string slug, string? description, ThemeWire? theme,
    string defaultLanguage, string[] supportedLanguages, string defaultCurrency, string? onboardingCompletedAt);
public sealed record RestOverviewWire(RestDetailWire restaurant);

[TestClass]
public sealed class MenuRestaurantWriteTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static async Task SeedRestaurant(Guid tenantId, string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        db.Restaurants.Add(new Restaurant
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Name = "Tasca", Slug = slug,
            DefaultLanguage = "en", SupportedLanguages = ["en"], DefaultCurrency = "EUR",
        });
        await db.SaveChangesAsync();
    }

    private object FullIdentity(object? overrides = null) => new
    {
        name = "Tasca do Zé",
        description = "A tavern",
        theme = new { primaryColor = "#8B0000", font = "inter", layout = "cards" },
        defaultLanguage = "en",
        supportedLanguages = new[] { "en", "pt" },
        defaultCurrency = "usd",
    };

    private async Task<(string token, string slug)> OwnerWithRestaurant(string email, string slug)
    {
        var (owner, tenantId) = await CreateOwnerWithTenant(email, Pw);
        await SeedRestaurant(tenantId, slug);
        return (owner.accessToken, slug);
    }

    private async Task<RestDetailWire> Overview(string slug, string bearer)
    {
        var resp = await Get($"/api/restaurants/{slug}", bearer);
        return (await resp.Content.ReadFromJsonAsync<RestOverviewWire>())!.restaurant;
    }

    [TestMethod]
    public async Task Updates_the_identity_and_returns_the_fresh_detail()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@a.pt", "a");

        var resp = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}", FullIdentity(), token);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<RestDetailWire>())!;

        Assert.AreEqual("Tasca do Zé", body.name);
        Assert.AreEqual("USD", body.defaultCurrency);     // normalized to upper-case
        CollectionAssert.AreEqual(new[] { "en", "pt" }, body.supportedLanguages);
        Assert.AreEqual("cards", body.theme!.layout);
    }

    [TestMethod]
    public async Task Rejects_an_unsupported_currency()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@b.pt", "b");
        var resp = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}", new
        {
            name = "X", defaultLanguage = "en", supportedLanguages = new[] { "en" }, defaultCurrency = "XYZ",
        }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Rejects_a_default_language_outside_the_supported_set()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@c.pt", "c");
        // default "en" not listed in supported ["pt"]
        var resp = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}", new
        {
            name = "X", defaultLanguage = "en", supportedLanguages = new[] { "pt" }, defaultCurrency = "EUR",
        }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Changing_the_default_language_is_not_yet_supported()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@d.pt", "d");
        var resp = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}", new
        {
            name = "X", defaultLanguage = "pt", supportedLanguages = new[] { "en", "pt" }, defaultCurrency = "EUR",
        }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Rejects_an_unknown_theme_font()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@e.pt", "e");
        var resp = await SendJson(HttpMethod.Patch, $"/api/restaurants/{slug}", new
        {
            name = "X", theme = new { font = "comic-sans" },
            defaultLanguage = "en", supportedLanguages = new[] { "en" }, defaultCurrency = "EUR",
        }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Renames_the_slug()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@f.pt", "old-slug");

        var resp = await PostJson($"/api/restaurants/{slug}/slug", new { slug = "new-slug" }, token);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        // The public page moves; the old slug 404s.
        Assert.AreEqual(HttpStatusCode.OK, (await Client.GetAsync("/public/r/new-slug")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync("/public/r/old-slug")).StatusCode);
    }

    [TestMethod]
    public async Task Rejects_an_invalid_slug()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@g.pt", "g");
        var resp = await PostJson($"/api/restaurants/{slug}/slug", new { slug = "Not Valid!" }, token);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Rejects_a_slug_already_in_use()
    {
        var (t1, _) = await OwnerWithRestaurant("owner@h1.pt", "taken");
        var (t2, slug2) = await OwnerWithRestaurant("owner@h2.pt", "mine");

        var resp = await PostJson($"/api/restaurants/{slug2}/slug", new { slug = "taken" }, t2);
        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [TestMethod]
    public async Task Completes_onboarding()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@i.pt", "i");
        Assert.IsNull((await Overview(slug, token)).onboardingCompletedAt);

        var resp = await PostBearer($"/api/restaurants/{slug}/complete-onboarding", token);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.IsNotNull((await Overview(slug, token)).onboardingCompletedAt);
    }

    [TestMethod]
    public async Task Deletes_the_restaurant()
    {
        var (token, slug) = await OwnerWithRestaurant("owner@j.pt", "j");
        var resp = await Delete($"/api/restaurants/{slug}", token);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Get($"/api/restaurants/{slug}", token)).StatusCode);
    }

    [TestMethod]
    public async Task A_different_tenant_cannot_update()
    {
        var (_, ownerTenant) = await CreateOwnerWithTenant("owner@k.pt", Pw);
        await SeedRestaurant(ownerTenant, "k");
        var (stranger, _) = await CreateOwnerWithTenant("stranger@k.pt", Pw);

        var resp = await SendJson(HttpMethod.Patch, "/api/restaurants/k", FullIdentity(), stranger.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
