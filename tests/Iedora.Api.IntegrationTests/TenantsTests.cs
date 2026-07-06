using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shape of POST /auth/tenants (camelCase).
public sealed record TenantPayload(string id, string name);

[TestClass]
public sealed class TenantsTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Create_tenant_without_token_returns_401()
    {
        var resp = await PostJson("/auth/tenants", new { name = "Tasca do Zé" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    [DataRow("")]      // empty → [Required] fails
    public async Task Create_tenant_with_invalid_name_returns_400(string name)
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson("/auth/tenants", new { name }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Create_tenant_returns_id_and_name_and_makes_caller_owner()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");

        var resp = await PostJson("/auth/tenants", new { name = "Tasca do Zé" }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var tenant = (await resp.Content.ReadFromJsonAsync<TenantPayload>())!;
        Assert.AreEqual("Tasca do Zé", tenant.name);
        Assert.IsTrue(Guid.TryParse(tenant.id, out var tenantId));

        // The caller now holds the sole owner membership for the tenant.
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.TenantId == tenantId);
        Assert.AreEqual(MembershipRoles.Owner, membership.Role);
        Assert.AreEqual(Guid.Parse(login.userId), membership.UserId);
    }

    [TestMethod]
    public async Task First_login_has_no_tenant_but_next_login_pins_the_created_one()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");
        Assert.IsNull(login.tenantId); // no memberships yet

        var created = (await (await PostJson("/auth/tenants", new { name = "Bistro" }, login.accessToken))
            .Content.ReadFromJsonAsync<TenantPayload>())!;

        // A fresh login re-resolves the default tenant from memberships and pins it on the session/token.
        var (body, _) = await Login("owner@tasca.pt", "Sup3rSecret!");
        Assert.AreEqual(Guid.Parse(created.id), Guid.Parse(body.tenantId!));
    }
}
