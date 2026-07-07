using System.Net;
using System.Net.Http.Json;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class SessionFlowTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Refresh_rotates_the_token()
    {
        await RegisterAccount("a@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("a@tasca.pt", "Sup3rSecret!");

        var resp = await Refresh(c1);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var c2 = RefreshCookieFrom(resp);
        Assert.IsNotNull(c2);
        Assert.AreNotEqual(c1, c2);                          // rotation ⇒ a brand-new token
        var body = (await resp.Content.ReadFromJsonAsync<TokenPayload>())!;
        Assert.IsNotEmpty(body.accessToken);
    }

    [TestMethod]
    public async Task Refresh_without_cookie_returns_401()
    {
        var bare = await Client.PostAsync("/auth/refresh", content: null);
        Assert.AreEqual(HttpStatusCode.Unauthorized, bare.StatusCode);
    }

    [TestMethod]
    public async Task Reusing_a_rotated_token_burns_the_whole_family()
    {
        await RegisterAccount("victim@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("victim@tasca.pt", "Sup3rSecret!");

        // Legit rotation: c1 → c2.
        var rotated = await Refresh(c1);
        Assert.AreEqual(HttpStatusCode.OK, rotated.StatusCode);
        var c2 = RefreshCookieFrom(rotated)!;

        // Replay the spent c1 → reuse detected, 401 with the machine-readable result code.
        var reuse = await Refresh(c1);
        Assert.AreEqual(HttpStatusCode.Unauthorized, reuse.StatusCode);
        using var problem = System.Text.Json.JsonDocument.Parse(await reuse.Content.ReadAsStringAsync());
        Assert.AreEqual("auth.refresh_token_reuse", problem.RootElement.GetProperty("code").GetString());

        // …and the family is burned, so the previously-valid c2 is now dead too.
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
    }

    [TestMethod]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        await RegisterAccount("bye@tasca.pt", "Sup3rSecret!");
        var (_, c1) = await Login("bye@tasca.pt", "Sup3rSecret!");

        var logout = await Logout(c1);
        Assert.AreEqual(HttpStatusCode.OK, logout.StatusCode);
        Assert.IsNull(RefreshCookieFrom(logout));            // cookie cleared (empty value)

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c1)).StatusCode);
    }

    [TestMethod]
    public async Task Logout_is_idempotent_for_an_unknown_token()
    {
        var resp = await Logout("this-is-not-a-real-token");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task Logout_all_revokes_every_session_of_the_user()
    {
        await RegisterAccount("multi@tasca.pt", "Sup3rSecret!");
        var (session1, c1) = await Login("multi@tasca.pt", "Sup3rSecret!"); // device 1
        var (_, c2) = await Login("multi@tasca.pt", "Sup3rSecret!");        // device 2

        var logoutAll = await PostBearer("/auth/logout-all", session1.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, logoutAll.StatusCode);

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c1)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
    }
}
