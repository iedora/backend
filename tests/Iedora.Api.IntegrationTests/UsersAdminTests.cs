using System.Net;
using System.Net.Http.Json;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class UsersAdminTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";
    private const string Temp = "T3mpPass!word";

    [TestMethod]
    public async Task Lists_a_users_sessions()
    {
        var target = await RegisterAndLogin("target@tasca.pt", Pw); // creates a session
        var svc = await ServiceToken();

        var resp = await Get($"/auth/admin/users/{target.userId}/sessions", svc);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var list = (await resp.Content.ReadFromJsonAsync<SessionsWire>())!;
        Assert.IsTrue(list.sessions.Length >= 1);
        Assert.IsTrue(list.sessions.Any(s => s.current));
    }

    [TestMethod]
    public async Task Listing_sessions_of_an_unknown_user_returns_404()
    {
        var svc = await ServiceToken();
        var resp = await Get($"/auth/admin/users/{Guid.NewGuid()}/sessions", svc);
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Revokes_a_users_device()
    {
        await RegisterAccount("dev@tasca.pt", Pw);
        var (access, refresh) = await Login("dev@tasca.pt", Pw);
        var svc = await ServiceToken();

        var listed = await Get($"/auth/admin/users/{access.userId}/sessions", svc);
        var family = (await listed.Content.ReadFromJsonAsync<SessionsWire>())!.sessions.First().familyId;

        var revoke = await PostBearer($"/auth/admin/users/{access.userId}/sessions/{family}/revoke", svc);
        Assert.AreEqual(HttpStatusCode.OK, revoke.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(refresh)).StatusCode);
    }

    [TestMethod]
    public async Task Revoking_an_unknown_device_returns_404()
    {
        var target = await RegisterAndLogin("dev2@tasca.pt", Pw);
        var svc = await ServiceToken();
        var revoke = await PostBearer($"/auth/admin/users/{target.userId}/sessions/{Guid.NewGuid()}/revoke", svc);
        Assert.AreEqual(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    [TestMethod]
    public async Task Force_password_change_revokes_sessions_and_flags_the_account()
    {
        await RegisterAccount("forced@tasca.pt", Pw);
        var (_, refresh) = await Login("forced@tasca.pt", Pw);
        var svc = await ServiceToken();

        var uid = (await Login("forced@tasca.pt", Pw)).body.userId;
        var accept = await PostBearer($"/auth/admin/users/{uid}/force-password-change", svc);
        await AwaitIdentityCommandAsync(accept);

        // Existing sessions are dead; the password still works but a change is now forced.
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(refresh)).StatusCode);
        var relogin = await Login("forced@tasca.pt", Pw);
        Assert.IsTrue(relogin.body.mustChangePassword);
    }

    [TestMethod]
    public async Task Set_password_installs_a_temporary_password()
    {
        var target = await RegisterAndLogin("setpw@tasca.pt", Pw);
        var svc = await ServiceToken();

        var accept = await PostJson($"/auth/admin/users/{target.userId}/set-password", new { password = Temp }, svc);
        await AwaitIdentityCommandAsync(accept);

        // Old password rejected; the temporary one works and forces a change.
        var old = await Client.PostAsJsonAsync("/auth/login", new { email = "setpw@tasca.pt", password = Pw });
        Assert.AreEqual(HttpStatusCode.Unauthorized, old.StatusCode);
        var (body, _) = await Login("setpw@tasca.pt", Temp);
        Assert.IsTrue(body.mustChangePassword);
    }

    [TestMethod]
    public async Task Set_password_rejects_a_weak_password()
    {
        var target = await RegisterAndLogin("weakset@tasca.pt", Pw);
        var svc = await ServiceToken();
        var resp = await PostJson($"/auth/admin/users/{target.userId}/set-password",
            new { password = "12345678" }, svc);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Admin_endpoints_reject_non_service_callers()
    {
        var user = await RegisterAndLogin("plain@tasca.pt", Pw);

        // A user access token is forbidden (Service policy requires typ=service).
        Assert.AreEqual(HttpStatusCode.Forbidden,
            (await Get($"/auth/admin/users/{user.userId}/sessions", user.accessToken)).StatusCode);
        // Anonymous is unauthorized.
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await Get($"/auth/admin/users/{user.userId}/sessions")).StatusCode);
    }
}
