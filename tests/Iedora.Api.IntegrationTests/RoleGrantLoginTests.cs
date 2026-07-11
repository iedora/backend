using System.Net;
using System.Net.Http.Json;
using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// ROLE_GRANTS is reconciled at login: a configured email holds its role in the issued token, with no
// code change and no manual DB grant (and it survives a disposable-staging reset). End-to-end proof
// that the grant reaches the token — register, then log in through a host configured with ROLE_GRANTS.
[TestClass]
public sealed class RoleGrantLoginTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    // A client whose host is configured with ROLE_GRANTS=<spec>, sharing the same database.
    private static HttpClient ClientWithGrants(string spec) =>
        TestHost.Factory.WithWebHostBuilder(b => b.UseSetting("ROLE_GRANTS", spec))
            .CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<WhoAmiPayload> LoginAndWhoAmi(HttpClient client, string email)
    {
        var login = await client.PostAsJsonAsync("/auth/login", new { email, password = Pw });
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<TokenPayload>())!.accessToken;

        var req = new HttpRequestMessage(HttpMethod.Get, "/auth/whoami");
        req.Headers.Authorization = new("Bearer", token);
        var who = await client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, who.StatusCode);
        return (await who.Content.ReadFromJsonAsync<WhoAmiPayload>())!;
    }

    [TestMethod]
    public async Task A_configured_email_is_granted_the_role_at_login()
    {
        await RegisterAccount("grantee@iedora.test", Pw);
        var who = await LoginAndWhoAmi(ClientWithGrants("admin=grantee@iedora.test"), "grantee@iedora.test");
        CollectionAssert.Contains(who.roles, Roles.Admin);
    }

    [TestMethod]
    public async Task A_non_configured_email_is_granted_nothing()
    {
        await RegisterAccount("other@iedora.test", Pw);
        var who = await LoginAndWhoAmi(ClientWithGrants("admin=grantee@iedora.test"), "other@iedora.test");
        Assert.AreEqual(0, who.roles.Length);
    }
}
