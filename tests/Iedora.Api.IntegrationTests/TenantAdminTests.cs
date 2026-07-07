using System.Net;
using System.Net.Http.Json;
using Iedora.Kernel;
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

        var createdId = await AwaitTenancyCommandAsync(
            await PostJson("/tenancy/admin/tenants", new { name = "Admin's Tasca", ownerUserId = target.userId }, admin.accessToken),
            admin.accessToken);

        // The target user (not the admin) is the owner.
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.TenantId == Guid.Parse(createdId));
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
    public async Task Transfer_saga_moves_the_tenant_to_a_brand_new_owner()
    {
        var oldOwner = await RegisterAndLogin("old@tasca.pt", "Sup3rSecret!");
        var tenantId = await CreateTenantAsync("Tasca", oldOwner.accessToken);
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");

        var accepted = (await (await PostJson($"/tenancy/admin/tenants/{tenantId}/transfer",
            new { email = "new@owner.pt", name = "New Owner" }, admin.accessToken))
            .Content.ReadFromJsonAsync<CommandAcceptedPayload>())!;

        // Hop 1: Tenancy outbox → Identity inbox (creates the user, emits UserProvisioned).
        await TestHost.DispatchTenancyOutboxAsync();
        // Hop 2: Identity outbox → Tenancy inbox (reassigns ownership, completes the command).
        await TestHost.DispatchOutboxAsync();

        Assert.AreEqual("Succeeded", (await GetCommandStatus(accepted.statusUrl, admin.accessToken)).status);

        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var owners = await db.Memberships
            .Where(m => m.TenantId == Guid.Parse(tenantId) && m.Role == MembershipRole.Owner)
            .ToListAsync();
        Assert.HasCount(1, owners);
        Assert.AreNotEqual(Guid.Parse(oldOwner.userId), owners[0].UserId);

        // The new owner is a real Identity user, created by the saga's hop-1 inbox handler.
        var identity = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var newUser = await identity.Users.SingleAsync(u => u.Id == owners[0].UserId);
        Assert.AreEqual("new@owner.pt", newUser.Email);
    }

    [TestMethod]
    public async Task Transfer_saga_with_a_taken_email_fails_the_command()
    {
        await RegisterAccount("taken@owner.pt", "Sup3rSecret!");
        var oldOwner = await RegisterAndLogin("old2@tasca.pt", "Sup3rSecret!");
        var tenantId = await CreateTenantAsync("Bistro", oldOwner.accessToken);
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");

        var accepted = (await (await PostJson($"/tenancy/admin/tenants/{tenantId}/transfer",
            new { email = "taken@owner.pt", name = "Nope" }, admin.accessToken))
            .Content.ReadFromJsonAsync<CommandAcceptedPayload>())!;

        await TestHost.DispatchTenancyOutboxAsync(); // Identity: create fails (email taken) → UserProvisioned(error)
        await TestHost.DispatchOutboxAsync();          // Tenancy: command Failed

        var status = await GetCommandStatus(accepted.statusUrl, admin.accessToken);
        Assert.AreEqual("Failed", status.status);
        Assert.AreEqual("auth.email_taken", status.errorCode);
    }

    [TestMethod]
    public async Task Transfer_of_unknown_tenant_returns_404()
    {
        var admin = await RegisterLoginAsAdmin("staff@iedora.com", "Sup3rSecret!");
        var resp = await PostJson($"/tenancy/admin/tenants/{Guid.NewGuid()}/transfer",
            new { email = "x@owner.pt", name = "X" }, admin.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Non_admin_cannot_transfer()
    {
        var user = await RegisterAndLogin("plain@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson($"/tenancy/admin/tenants/{Guid.NewGuid()}/transfer",
            new { email = "x@owner.pt", name = "X" }, user.accessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
