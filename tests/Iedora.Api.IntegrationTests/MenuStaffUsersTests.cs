using System.Net;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

public sealed record AdminUserWire(string id, string email, string? name, string[] roles, string createdAt, string? passwordChangedAt, bool emailConfirmed, bool mustChangePassword, int tenantCount);
public sealed record UserListWire(AdminUserWire[] users);
public sealed record MembershipWire(string tenantId, string role);
public sealed record UserSessionWire(string id, string familyId, string? tenantId, bool current);
public sealed record UserDetailWire(AdminUserWire user, MembershipWire[] memberships, UserSessionWire[] sessions);

[TestClass]
public sealed class MenuStaffUsersTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private async Task<UserDetailWire> Detail(Guid id, string admin) =>
        (await (await Get($"/api/staff/users/{id}", admin)).Content.ReadFromJsonAsync<UserDetailWire>())!;

    [TestMethod]
    public async Task Lists_users_with_tenant_counts_and_search()
    {
        var (owner, _) = await CreateOwnerWithTenant("dana@corp.pt", Pw, "Dana Co");
        await CreateOwnerWithTenant("erin@corp.pt", Pw, "Erin Co");
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);

        var all = (await (await Get("/api/staff/users", admin.accessToken)).Content.ReadFromJsonAsync<UserListWire>())!;
        Assert.IsTrue(all.users.Length >= 3); // two owners + the admin

        var dana = all.users.Single(u => u.email == "dana@corp.pt");
        Assert.AreEqual(1, dana.tenantCount);
        Assert.AreEqual(owner.userId, dana.id);

        var hit = (await (await Get("/api/staff/users?q=erin", admin.accessToken)).Content.ReadFromJsonAsync<UserListWire>())!;
        Assert.HasCount(1, hit.users);
        Assert.AreEqual("erin@corp.pt", hit.users[0].email);
    }

    [TestMethod]
    public async Task Admin_role_shows_up_on_the_record()
    {
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);
        var detail = await Detail(Guid.Parse(admin.userId), admin.accessToken);
        CollectionAssert.Contains(detail.user.roles, "admin");
    }

    [TestMethod]
    public async Task Detail_carries_memberships_and_sessions()
    {
        var (owner, tenantId) = await CreateOwnerWithTenant("owner@corp.pt", Pw);
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);

        var d = await Detail(Guid.Parse(owner.userId), admin.accessToken);
        Assert.AreEqual("owner@corp.pt", d.user.email);
        Assert.HasCount(1, d.memberships);
        Assert.AreEqual(tenantId.ToString(), d.memberships[0].tenantId);
        Assert.IsTrue(d.sessions.Any(s => s.current)); // the owner's login left a live session
    }

    [TestMethod]
    public async Task Force_password_change_flags_the_user_and_kills_sessions()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@corp.pt", Pw);
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);
        var id = Guid.Parse(owner.userId);

        Assert.AreEqual(HttpStatusCode.NoContent, (await PostBearer($"/api/staff/users/{id}/force-password-change", admin.accessToken)).StatusCode);

        var d = await Detail(id, admin.accessToken);
        Assert.IsTrue(d.user.mustChangePassword);
        Assert.IsFalse(d.sessions.Any(s => s.current)); // every device signed out
    }

    [TestMethod]
    public async Task Set_password_accepts_a_strong_one_and_rejects_a_weak_one()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@corp.pt", Pw);
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);
        var id = Guid.Parse(owner.userId);

        Assert.AreEqual(HttpStatusCode.BadRequest,
            (await PostJson($"/api/staff/users/{id}/set-password", new { password = "abc" }, admin.accessToken)).StatusCode); // < 8

        Assert.AreEqual(HttpStatusCode.NoContent,
            (await PostJson($"/api/staff/users/{id}/set-password", new { password = "T3mpPass!" }, admin.accessToken)).StatusCode);

        var d = await Detail(id, admin.accessToken);
        Assert.IsTrue(d.user.mustChangePassword);       // temp password forces a change
        Assert.IsNotNull(d.user.passwordChangedAt);     // stamped
        Assert.IsFalse(d.sessions.Any(s => s.current)); // signed out
    }

    [TestMethod]
    public async Task Revokes_a_single_session()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@corp.pt", Pw);
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);
        var id = Guid.Parse(owner.userId);
        // The owner logged in twice (register + re-login) → two families; revoke just one.
        var family = (await Detail(id, admin.accessToken)).sessions.First(s => s.current).familyId;

        Assert.AreEqual(HttpStatusCode.NoContent, (await PostBearer($"/api/staff/users/{id}/sessions/{family}/revoke", admin.accessToken)).StatusCode);
        Assert.IsFalse((await Detail(id, admin.accessToken)).sessions.Any(s => s.familyId == family && s.current)); // that device is out

        // An unknown family isn't a live session → 404.
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostBearer($"/api/staff/users/{id}/sessions/{Guid.NewGuid()}/revoke", admin.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task Unknown_user_404s()
    {
        var admin = await RegisterLoginAsAdmin("root@corp.pt", Pw);
        var missing = Guid.NewGuid();
        Assert.AreEqual(HttpStatusCode.NotFound, (await Get($"/api/staff/users/{missing}", admin.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostBearer($"/api/staff/users/{missing}/force-password-change", admin.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await PostJson($"/api/staff/users/{missing}/set-password", new { password = "T3mpPass!" }, admin.accessToken)).StatusCode);
    }

    [TestMethod]
    public async Task The_users_crm_is_admin_only()
    {
        var (owner, _) = await CreateOwnerWithTenant("owner@corp.pt", Pw);
        Assert.AreEqual(HttpStatusCode.Forbidden, (await Get("/api/staff/users", owner.accessToken)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Get("/api/staff/users")).StatusCode);
    }
}
