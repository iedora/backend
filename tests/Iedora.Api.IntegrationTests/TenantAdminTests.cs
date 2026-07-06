using System.Net;
using System.Net.Http.Json;
using Iedora.Api.Shared;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shapes of the admin tenant reads (camelCase).
public sealed record OwnerPayload(string id, string email, string? name);
public sealed record TenantOwnerPayload(string id, string name, OwnerPayload owner);
public sealed record TenantListPayload(TenantOwnerPayload[] tenants);
public sealed record TransferPayload(string ownerId);

[TestClass]
public sealed class TenantAdminTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Admin_lists_tenants_with_their_owners()
    {
        var owner = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");
        var createdId = await CreateTenantAsync("Tasca do Zé", owner.accessToken);

        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await Get("/tenancy/admin/tenants", admin.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var list = (await resp.Content.ReadFromJsonAsync<TenantListPayload>())!;
        var tenant = list.tenants.Single(t => t.id == createdId);
        Assert.AreEqual("Tasca do Zé", tenant.name);
        Assert.AreEqual(owner.userId, tenant.owner.id);
        Assert.AreEqual("owner@tasca.pt", tenant.owner.email); // owner user resolved via IIdentityApi
    }

    [TestMethod]
    public async Task Get_by_id_returns_the_tenant_with_owner()
    {
        var owner = await RegisterAndLogin("owner@bistro.pt", "Sup3rSecret!");
        var createdId = await CreateTenantAsync("Bistro", owner.accessToken);

        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await Get($"/tenancy/admin/tenants/{createdId}", admin.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var tenant = (await resp.Content.ReadFromJsonAsync<TenantOwnerPayload>())!;
        Assert.AreEqual("Bistro", tenant.name);
        Assert.AreEqual("owner@bistro.pt", tenant.owner.email);
    }

    [TestMethod]
    public async Task Non_admin_is_forbidden()
    {
        var user = await RegisterAndLogin("plain@tasca.pt", "Sup3rSecret!");
        var resp = await Get("/tenancy/admin/tenants", user.accessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_is_unauthorized()
    {
        var resp = await Get("/tenancy/admin/tenants");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Get_unknown_tenant_returns_404()
    {
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await Get($"/tenancy/admin/tenants/{Guid.NewGuid()}", admin.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Admin_creates_a_tenant_for_an_existing_user()
    {
        var target = await RegisterAndLogin("target@tasca.pt", "Sup3rSecret!");
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");

        var resp = await PostJson("/tenancy/admin/tenants",
            new { name = "Admin's Tasca", ownerUserId = target.userId }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var created = (await resp.Content.ReadFromJsonAsync<TenantPayload>())!;

        // The target user (not the admin) is the owner.
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.TenantId == Guid.Parse(created.id));
        Assert.AreEqual(MembershipRole.Owner, membership.Role);
        Assert.AreEqual(Guid.Parse(target.userId), membership.UserId);
    }

    [TestMethod]
    public async Task Admin_create_with_unknown_owner_returns_400()
    {
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await PostJson("/tenancy/admin/tenants",
            new { name = "Ghost", ownerUserId = Guid.NewGuid() }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Non_admin_cannot_create_for_others()
    {
        var user = await RegisterAndLogin("plain@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson("/tenancy/admin/tenants",
            new { name = "Nope", ownerUserId = user.userId }, user.accessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Transfer_moves_the_tenant_to_a_brand_new_owner()
    {
        var oldOwner = await RegisterAndLogin("old@tasca.pt", "Sup3rSecret!");
        var tenantId = await CreateTenantAsync("Tasca", oldOwner.accessToken);
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");

        var resp = await PostJson($"/tenancy/admin/tenants/{tenantId}/transfer",
            new { email = "new@owner.pt", name = "New Owner", password = "N3wOwnerPass!" }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TransferPayload>())!;

        // Sole owner is the new user; the old owner's membership is gone.
        await using (var scope = TestHost.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
            var owners = await db.Memberships
                .Where(m => m.TenantId == Guid.Parse(tenantId) && m.Role == MembershipRole.Owner)
                .ToListAsync();
            Assert.HasCount(1, owners);
            Assert.AreEqual(Guid.Parse(body.ownerId), owners[0].UserId);
            Assert.AreNotEqual(Guid.Parse(oldOwner.userId), owners[0].UserId);
        }

        // The new owner can log in, and the transferred tenant is their default.
        var (login, _) = await Login("new@owner.pt", "N3wOwnerPass!");
        Assert.AreEqual(tenantId, login.tenantId);
    }

    [TestMethod]
    public async Task Transfer_to_a_taken_email_returns_409()
    {
        await Register("taken@owner.pt", "Sup3rSecret!");
        var oldOwner = await RegisterAndLogin("old2@tasca.pt", "Sup3rSecret!");
        var tenantId = await CreateTenantAsync("Bistro", oldOwner.accessToken);
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");

        var resp = await PostJson($"/tenancy/admin/tenants/{tenantId}/transfer",
            new { email = "taken@owner.pt", name = "Nope", password = "N3wOwnerPass!" }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [TestMethod]
    public async Task Transfer_of_unknown_tenant_returns_404()
    {
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await PostJson($"/tenancy/admin/tenants/{Guid.NewGuid()}/transfer",
            new { email = "x@owner.pt", name = "X", password = "N3wOwnerPass!" }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Non_admin_cannot_transfer()
    {
        var user = await RegisterAndLogin("plain@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson($"/tenancy/admin/tenants/{Guid.NewGuid()}/transfer",
            new { email = "x@owner.pt", name = "X", password = "N3wOwnerPass!" }, user.accessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    /// <summary>Register, grant the admin role (so the JWT carries it), and log in.</summary>
    private async Task<TokenPayload> RegisterLoginAsAdmin(string email, string password)
    {
        Assert.AreEqual(HttpStatusCode.Created, (await Register(email, password)).StatusCode);
        await using (var scope = TestHost.Factory.Services.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
            if (!await roles.RoleExistsAsync(Roles.Admin))
                await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin));
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            await users.AddToRoleAsync(user, Roles.Admin);
        }
        return (await Login(email, password)).body;
    }
}
