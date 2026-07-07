using System.Net;
using System.Net.Http.Json;
using Iedora.Identity;
using Iedora.Menus;
using Iedora.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Api.IntegrationTests;

public sealed record QrViewWire(string code, string? restaurantId, string? restaurantName, string? restaurantSlug, string? label, string? boundAt);
public sealed record QrListWire(QrViewWire[] codes);
public sealed record CreateQrWire(int inserted);
public sealed record QrTargetWire(string slug);

[TestClass]
public sealed class MenuQrTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static async Task<Guid> SeedRestaurant(string slug)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MenuDbContext>();
        var id = Guid.CreateVersion7();
        db.Restaurants.Add(new Restaurant
        {
            Id = id, TenantId = Guid.NewGuid(), Name = "Tasca", Slug = slug,
            DefaultLanguage = "en", SupportedLanguages = ["en"], DefaultCurrency = "EUR",
        });
        await db.SaveChangesAsync();
        return id;
    }

    private async Task<QrViewWire[]> List(string staff) =>
        (await (await Get("/api/staff/qr-codes", staff)).Content.ReadFromJsonAsync<QrListWire>())!.codes;

    [TestMethod]
    public async Task Staff_creates_an_explicit_code_and_it_appears_unbound()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);

        var resp = await PostJson("/api/staff/qr-codes", new { code = "Table-1" }, staff.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        Assert.AreEqual(1, (await resp.Content.ReadFromJsonAsync<CreateQrWire>())!.inserted);

        var code = (await List(staff.accessToken)).Single();
        Assert.AreEqual("table-1", code.code); // normalized to lowercase
        Assert.IsNull(code.boundAt);
    }

    [TestMethod]
    public async Task Creating_a_batch_inserts_that_many_and_is_idempotent()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);

        var batch = await PostJson("/api/staff/qr-codes", new { count = 3 }, staff.accessToken);
        Assert.AreEqual(3, (await batch.Content.ReadFromJsonAsync<CreateQrWire>())!.inserted);

        // Re-inserting an existing explicit code is a no-op.
        await PostJson("/api/staff/qr-codes", new { code = "dup" }, staff.accessToken);
        var again = await PostJson("/api/staff/qr-codes", new { code = "dup" }, staff.accessToken);
        Assert.AreEqual(0, (await again.Content.ReadFromJsonAsync<CreateQrWire>())!.inserted);
    }

    [TestMethod]
    public async Task Invalid_code_is_rejected()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        var resp = await PostJson("/api/staff/qr-codes", new { code = "no spaces!" }, staff.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Bind_then_the_public_scan_resolves_to_the_slug()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        var restId = await SeedRestaurant("tasca");
        await PostJson("/api/staff/qr-codes", new { code = "t1" }, staff.accessToken);

        // Unbound → public scan 404.
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync("/public/qr/t1")).StatusCode);

        var bind = await PostJson("/api/staff/qr-codes/t1/bind", new { restaurantId = restId }, staff.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, bind.StatusCode);

        var target = await Client.GetAsync("/public/qr/T1"); // scan is case-insensitive (normalized)
        Assert.AreEqual(HttpStatusCode.OK, target.StatusCode);
        Assert.AreEqual("tasca", (await target.Content.ReadFromJsonAsync<QrTargetWire>())!.slug);
    }

    [TestMethod]
    public async Task Create_can_pre_bind_and_label()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        var restId = await SeedRestaurant("bistro");

        await PostJson("/api/staff/qr-codes",
            new { code = "door", restaurantId = restId, label = "Front door" }, staff.accessToken);

        var code = (await List(staff.accessToken)).Single();
        Assert.AreEqual("Front door", code.label);
        Assert.AreEqual("bistro", code.restaurantSlug);
        Assert.IsNotNull(code.boundAt);
        Assert.AreEqual("bistro", (await (await Client.GetAsync("/public/qr/door")).Content.ReadFromJsonAsync<QrTargetWire>())!.slug);
    }

    [TestMethod]
    public async Task Unbind_frees_the_sticker()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        var restId = await SeedRestaurant("tasca");
        await PostJson("/api/staff/qr-codes", new { code = "t1", restaurantId = restId }, staff.accessToken);

        var unbind = await PostBearer("/api/staff/qr-codes/t1/unbind", staff.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, unbind.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync("/public/qr/t1")).StatusCode);
    }

    [TestMethod]
    public async Task Delete_removes_the_sticker()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        await PostJson("/api/staff/qr-codes", new { code = "gone" }, staff.accessToken);

        var del = await Delete("/api/staff/qr-codes/gone", staff.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
        var bind = await PostJson("/api/staff/qr-codes/gone/bind", new { restaurantId = Guid.NewGuid() }, staff.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, bind.StatusCode); // unknown restaurant checked first
    }

    [TestMethod]
    public async Task Bind_to_an_unknown_restaurant_is_rejected()
    {
        var staff = await RegisterLoginAsAdmin("staff@iedora.com", Pw);
        await PostJson("/api/staff/qr-codes", new { code = "t1" }, staff.accessToken);
        var bind = await PostJson("/api/staff/qr-codes/t1/bind", new { restaurantId = Guid.NewGuid() }, staff.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, bind.StatusCode);
    }

    [TestMethod]
    public async Task Malformed_public_scan_is_404()
    {
        Assert.AreEqual(HttpStatusCode.NotFound, (await Client.GetAsync("/public/qr/has%20space")).StatusCode);
    }

    [TestMethod]
    public async Task Staff_surface_requires_the_admin_role()
    {
        var owner = await RegisterAndLogin("owner@tasca.pt", Pw); // a plain user
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Get("/api/staff/qr-codes", owner.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Get("/api/staff/qr-codes")).StatusCode);
    }
}
