using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shape of POST /tenancy/tenants (camelCase).
public sealed record TenantPayload(string id, string name);

[TestClass]
public sealed class TenantsTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Create_tenant_without_token_returns_401()
    {
        var resp = await PostJson("/tenancy/tenants", new { name = "Tasca do Zé" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    [DataRow("")]      // empty → [Required] fails
    public async Task Create_tenant_with_invalid_name_returns_400(string name)
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson("/tenancy/tenants", new { name }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Create_tenant_is_202_then_status_goes_pending_to_succeeded()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");

        var accept = await PostJson("/tenancy/tenants", new { name = "Tasca" }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.Accepted, accept.StatusCode);
        var accepted = (await accept.Content.ReadFromJsonAsync<CommandAcceptedPayload>())!;

        Assert.AreEqual("Pending", (await GetCommandStatus(accepted.statusUrl, login.accessToken)).status);

        await TestHost.DispatchTenancyOutboxAsync();

        var done = await GetCommandStatus(accepted.statusUrl, login.accessToken);
        Assert.AreEqual("Succeeded", done.status);
        Assert.IsTrue(done.resultLocation!.StartsWith("/tenancy/tenants/"));
    }

    [TestMethod]
    public async Task Create_tenant_is_accepted_then_makes_the_caller_owner()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");

        // 202 → dispatch the handler → status Succeeded (asserted inside the helper); returns the id.
        var tenantId = await CreateTenantAsync("Tasca do Zé", login.accessToken);

        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TenancyDbContext>();
        var membership = await db.Memberships.SingleAsync(m => m.TenantId == Guid.Parse(tenantId));
        Assert.AreEqual(MembershipRole.Owner, membership.Role);
        Assert.AreEqual(Guid.Parse(login.userId), membership.UserId);
    }

    [TestMethod]
    public async Task First_login_has_no_tenant_but_next_login_pins_the_created_one()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", "Sup3rSecret!");
        Assert.IsNull(login.tenantId); // no memberships yet

        var tenantId = await CreateTenantAsync("Bistro", login.accessToken);

        // A fresh login re-resolves the default tenant from memberships and pins it on the session/token.
        var (body, _) = await Login("owner@tasca.pt", "Sup3rSecret!");
        Assert.AreEqual(Guid.Parse(tenantId), Guid.Parse(body.tenantId!));
    }
}
