using System.Net;
using System.Net.Http.Json;

namespace Iedora.Api.IntegrationTests;

// Wire shape of the device-history rows (only the fields the tests read; extras are ignored).
public sealed record SessionWire(string familyId, bool current);
public sealed record SessionsWire(SessionWire[] sessions);

[TestClass]
public sealed class AccountSessionsTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    [TestMethod]
    public async Task Lists_the_users_live_sessions()
    {
        await RegisterAccount("multi@tasca.pt", Pw);
        var (d1, _) = await Login("multi@tasca.pt", Pw);
        await Login("multi@tasca.pt", Pw); // a second device

        var resp = await Get("/auth/sessions", d1.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var list = (await resp.Content.ReadFromJsonAsync<SessionsWire>())!;

        Assert.HasCount(2, list.sessions);
        Assert.IsTrue(list.sessions.All(s => s.current)); // both freshly logged in → live
    }

    [TestMethod]
    public async Task Revoking_a_family_kills_that_devices_refresh()
    {
        await RegisterAccount("kick@tasca.pt", Pw);
        var (access, refresh) = await Login("kick@tasca.pt", Pw);

        var listed = await Get("/auth/sessions", access.accessToken);
        var family = (await listed.Content.ReadFromJsonAsync<SessionsWire>())!.sessions.Single().familyId;

        var revoke = await PostBearer($"/auth/sessions/{family}/revoke", access.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);

        // That device's refresh token no longer works.
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(refresh)).StatusCode);
    }

    [TestMethod]
    public async Task Revoking_an_unknown_family_returns_404()
    {
        var login = await RegisterAndLogin("nodev@tasca.pt", Pw);
        var revoke = await PostBearer($"/auth/sessions/{Guid.NewGuid()}/revoke", login.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [TestMethod]
    public async Task A_user_cannot_revoke_another_users_device()
    {
        var victim = await RegisterAndLogin("victim@tasca.pt", Pw);
        var (_, victimRefresh) = await Login("victim@tasca.pt", Pw);
        // The victim's real, live family id (newest session = the second login above).
        var victimList = await Get("/auth/sessions", victim.accessToken);
        var victimFamily = (await victimList.Content.ReadFromJsonAsync<SessionsWire>())!.sessions.First().familyId;

        var attacker = await RegisterAndLogin("attacker@tasca.pt", Pw);
        // Owner-scoped: the attacker holds the victim's family id but it isn't theirs → 404, no effect.
        var revoke = await PostBearer($"/auth/sessions/{victimFamily}/revoke", attacker.accessToken);
        Assert.AreEqual(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Refresh(victimRefresh)).StatusCode); // victim still alive
    }

    [TestMethod]
    public async Task Revoke_others_signs_out_every_device_but_the_caller()
    {
        await RegisterAccount("me@tasca.pt", Pw);
        var (d1, c1) = await Login("me@tasca.pt", Pw); // the current device
        var (_, c2) = await Login("me@tasca.pt", Pw);  // another device

        var resp = await PostBearer("/auth/sessions/revoke-others", d1.accessToken);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        // The other device is dead; the caller's own session still refreshes.
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await Refresh(c1)).StatusCode);
    }

    [TestMethod]
    public async Task Sessions_endpoints_require_authentication()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Get("/auth/sessions")).StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await PostBearer("/auth/sessions/revoke-others", null)).StatusCode);
    }
}
