using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Iedora.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Menu's asset attach: upload to /media/images, then PUT the returned URL onto a restaurant/item.
[TestClass]
public sealed class MenuAssetTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static async Task<(string slug, Guid restId, Guid itemId)> Seed(Guid tenantId, string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var restId = Guid.CreateVersion7();
        var menuId = Guid.CreateVersion7();
        var catId = Guid.CreateVersion7();
        var itemId = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant { Id = restId, TenantId = tenantId, Name = "Tasca", Slug = slug, DefaultLanguage = "en", SupportedLanguages = ["en"], DefaultCurrency = "EUR" });
        db.Menus.Add(new Menu { Id = menuId, RestaurantId = restId, Name = "Lunch", Position = 0, Active = true });
        db.Categories.Add(new Category { Id = catId, MenuId = menuId, RestaurantId = restId, Name = "Starters", Position = 0 });
        db.Items.Add(new Item { Id = itemId, CategoryId = catId, RestaurantId = restId, Name = "Soup", PriceCents = 500, Position = 0, Available = true });
        await db.SaveChangesAsync();
        return (slug, restId, itemId);
    }

    private async Task<string> UploadImage(string token)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(Png);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(part, "file", "f.png");
        var req = new HttpRequestMessage(HttpMethod.Post, "/media/images") { Content = form };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await Client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<ImageUploadWire>())!.publicUrl;
    }

    private async Task<HttpResponseMessage> PutJson(string path, object body, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, path) { Content = JsonContent.Create(body) };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Client.SendAsync(req);
    }

    private static Task<string?> LogoUrl(Guid restId) =>
        Query(db => db.Restaurants.Where(r => r.Id == restId).Select(r => r.LogoUrl).SingleAsync());

    private static async Task<T> Query<T>(Func<MenuDbContext, Task<T>> q)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        return await q(scope.ServiceProvider.GetRequiredService<MenuDbContext>());
    }

    [TestMethod]
    public async Task Attaching_a_logo_persists_the_url_and_keeps_it_served()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a1.pt", Pw);
        var (slug, restId, _) = await Seed(tenantId, "tasca");
        var url = await UploadImage(owner.accessToken);

        var resp = await PutJson($"/api/restaurants/{slug}/logo", new { url }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.AreEqual(url, await LogoUrl(restId));
        Assert.AreEqual(HttpStatusCode.OK, (await Client.GetAsync(url)).StatusCode);
    }

    [TestMethod]
    public async Task Re_attaching_deletes_the_previously_attached_object()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a2.pt", Pw);
        var (slug, _, _) = await Seed(tenantId, "tasca");
        var first = await UploadImage(owner.accessToken);
        var second = await UploadImage(owner.accessToken);

        await PutJson($"/api/restaurants/{slug}/banner", new { url = first }, owner.accessToken);
        await PutJson($"/api/restaurants/{slug}/banner", new { url = second }, owner.accessToken);

        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync(first)).StatusCode);  // old object gone
        Assert.AreEqual(HttpStatusCode.OK, (await Client.GetAsync(second)).StatusCode);
    }

    [TestMethod]
    public async Task Clearing_nulls_the_column_and_deletes_the_object()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a3.pt", Pw);
        var (slug, restId, _) = await Seed(tenantId, "tasca");
        var url = await UploadImage(owner.accessToken);
        await PutJson($"/api/restaurants/{slug}/logo", new { url }, owner.accessToken);

        var resp = await PutJson($"/api/restaurants/{slug}/logo", new { url = (string?)null }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        Assert.IsNull(await LogoUrl(restId));
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync(url)).StatusCode);
    }

    [TestMethod]
    public async Task A_foreign_or_other_tenant_url_is_rejected()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a4.pt", Pw);
        var (slug, _, _) = await Seed(tenantId, "tasca");

        // Not one of ours at all.
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await PutJson($"/api/restaurants/{slug}/logo", new { url = "https://evil.example/x.png" }, owner.accessToken)).StatusCode);
        // A media URL, but scoped to a different tenant.
        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await PutJson($"/api/restaurants/{slug}/logo", new { url = $"/media/t/{Guid.NewGuid()}/x.png" }, owner.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task Item_photo_attaches_for_an_owned_item_and_404s_for_a_foreign_one()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a5.pt", Pw);
        var (slug, _, itemId) = await Seed(tenantId, "tasca");
        var url = await UploadImage(owner.accessToken);

        var ok = await PutJson($"/api/restaurants/{slug}/items/{itemId}/image", new { url }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, ok.StatusCode);
        var stored = await Query(db => db.Items.Where(i => i.Id == itemId).Select(i => i.ImageUrl).SingleAsync());
        Assert.AreEqual(url, stored);

        var foreign = await PutJson($"/api/restaurants/{slug}/items/{Guid.NewGuid()}/image", new { url }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [TestMethod]
    public async Task Attaching_requires_authentication()
    {
        var (_, tenantId) = await CreateOwnerWithTenant("owner@a6.pt", Pw);
        var (slug, _, _) = await Seed(tenantId, "tasca");
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/restaurants/{slug}/logo") { Content = JsonContent.Create(new { url = (string?)null }) };
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client.SendAsync(req)).StatusCode);
    }
}
