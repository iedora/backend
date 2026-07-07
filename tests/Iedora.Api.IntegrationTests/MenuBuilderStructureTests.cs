using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record CreatedIdWire(string id);

[TestClass]
public sealed class MenuBuilderStructureTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    // Seed a bare restaurant owned by tenantId; return its slug.
    private static async Task SeedRestaurant(Guid tenantId, string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        db.Restaurants.Add(new Restaurant
        {
            Id = Guid.CreateVersion7(), TenantId = tenantId, Name = "Tasca", Slug = slug,
            DefaultLanguage = "en", SupportedLanguages = ["en", "pt"], DefaultCurrency = "EUR",
        });
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateMenu(string slug, string bearer, string name = "Lunch")
    {
        var resp = await PostJson($"/api/restaurants/{slug}/menus", new { name }, bearer);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        return Guid.Parse((await resp.Content.ReadFromJsonAsync<CreatedIdWire>())!.id);
    }

    private async Task<Guid> CreateCategory(string slug, Guid menuId, string bearer, string name = "Starters")
    {
        var resp = await PostJson($"/api/restaurants/{slug}/menus/{menuId}/categories", new { name }, bearer);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        return Guid.Parse((await resp.Content.ReadFromJsonAsync<CreatedIdWire>())!.id);
    }

    private async Task<MenuTreeWire> Tree(string slug, string bearer) =>
        (await (await Get($"/api/restaurants/{slug}/tree", bearer)).Content.ReadFromJsonAsync<MenuTreeWire>())!;

    [TestMethod]
    public async Task Creates_menu_and_category_visible_in_the_tree()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@tasca.pt", Pw);
        await SeedRestaurant(tenantId, "tasca");

        var menuId = await CreateMenu("tasca", owner.accessToken);
        await CreateCategory("tasca", menuId, owner.accessToken);

        var tree = await Tree("tasca", owner.accessToken);
        Assert.HasCount(1, tree.menus);
        Assert.AreEqual("Lunch", tree.menus[0].name);
        Assert.HasCount(1, tree.menus[0].categories);
        Assert.AreEqual("Starters", tree.menus[0].categories[0].name);
    }

    [TestMethod]
    public async Task Created_menus_append_at_the_next_position()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@a.pt", Pw);
        await SeedRestaurant(tenantId, "a");

        await CreateMenu("a", owner.accessToken, "First");
        await CreateMenu("a", owner.accessToken, "Second");

        var tree = await Tree("a", owner.accessToken);
        CollectionAssert.AreEqual(new[] { "First", "Second" }, tree.menus.Select(m => m.name).ToArray());
    }

    [TestMethod]
    public async Task Updates_a_menu()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@b.pt", Pw);
        await SeedRestaurant(tenantId, "b");
        var menuId = await CreateMenu("b", owner.accessToken);

        var patch = await SendJson(HttpMethod.Patch, $"/api/restaurants/b/menus/{menuId}",
            new { name = "Dinner", active = false, nameI18n = new { pt = "Jantar" } }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, patch.StatusCode);

        var tree = await Tree("b", owner.accessToken);
        Assert.AreEqual("Dinner", tree.menus[0].name);
        Assert.IsFalse(tree.menus[0].active);
    }

    [TestMethod]
    public async Task Deletes_a_menu_and_its_categories()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@c.pt", Pw);
        await SeedRestaurant(tenantId, "c");
        var menuId = await CreateMenu("c", owner.accessToken);
        await CreateCategory("c", menuId, owner.accessToken);

        var del = await Delete($"/api/restaurants/c/menus/{menuId}", owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        Assert.IsEmpty((await Tree("c", owner.accessToken)).menus);
    }

    [TestMethod]
    public async Task Reorders_categories()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@d.pt", Pw);
        await SeedRestaurant(tenantId, "d");
        var menuId = await CreateMenu("d", owner.accessToken);
        var a = await CreateCategory("d", menuId, owner.accessToken, "A");
        var b = await CreateCategory("d", menuId, owner.accessToken, "B");
        var cId = await CreateCategory("d", menuId, owner.accessToken, "C");

        var reorder = await SendJson(HttpMethod.Put, $"/api/restaurants/d/menus/{menuId}/category-order",
            new { orderedIds = new[] { cId, b, a } }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, reorder.StatusCode);

        var cats = (await Tree("d", owner.accessToken)).menus[0].categories.Select(x => x.name).ToArray();
        CollectionAssert.AreEqual(new[] { "C", "B", "A" }, cats);
    }

    [TestMethod]
    public async Task Reorder_rejects_a_partial_list()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@e.pt", Pw);
        await SeedRestaurant(tenantId, "e");
        var menuId = await CreateMenu("e", owner.accessToken);
        var a = await CreateCategory("e", menuId, owner.accessToken, "A");
        await CreateCategory("e", menuId, owner.accessToken, "B");

        var reorder = await SendJson(HttpMethod.Put, $"/api/restaurants/e/menus/{menuId}/category-order",
            new { orderedIds = new[] { a } }, owner.accessToken); // missing B
        Assert.AreEqual(HttpStatusCode.BadRequest, reorder.StatusCode);
    }

    [TestMethod]
    public async Task Blank_name_is_rejected()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@f.pt", Pw);
        await SeedRestaurant(tenantId, "f");
        var resp = await PostJson("/api/restaurants/f/menus", new { name = "   " }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Category_under_an_unknown_menu_is_404()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@g.pt", Pw);
        await SeedRestaurant(tenantId, "g");
        var resp = await PostJson($"/api/restaurants/g/menus/{Guid.NewGuid()}/categories",
            new { name = "X" }, owner.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task A_different_tenant_cannot_create_a_menu()
    {
        var (_, ownerTenant) = await CreateOwnerWithTenant("owner@h.pt", Pw);
        await SeedRestaurant(ownerTenant, "h");
        var (stranger, _) = await CreateOwnerWithTenant("stranger@h.pt", Pw);

        var resp = await PostJson("/api/restaurants/h/menus", new { name = "Nope" }, stranger.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Staff_can_edit_any_restaurant()
    {
        var (_, ownerTenant) = await CreateOwnerWithTenant("owner@i.pt", Pw);
        await SeedRestaurant(ownerTenant, "i");
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", Pw);

        var resp = await PostJson("/api/restaurants/i/menus", new { name = "Staff Menu" }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }
}
