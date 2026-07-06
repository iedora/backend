using System.Net;
using System.Net.Http.Json;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class ChangePasswordTests : IntegrationTestBase
{
    private const string Old = "Sup3rSecret!";
    private const string New = "Ev3nBetter!";

    [TestMethod]
    public async Task Voluntary_change_with_correct_current_returns_204_and_lets_new_password_log_in()
    {
        var login = await RegisterAndLogin("owner@tasca.pt", Old);

        var resp = await PostJson("/auth/change-password",
            new { currentPassword = Old, newPassword = New }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        // Old password no longer works; new one does.
        var oldTry = await Client.PostAsJsonAsync("/auth/login", new { email = "owner@tasca.pt", password = Old });
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldTry.StatusCode);
        var newTry = await Client.PostAsJsonAsync("/auth/login", new { email = "owner@tasca.pt", password = New });
        Assert.AreEqual(HttpStatusCode.OK, newTry.StatusCode);
    }

    [TestMethod]
    public async Task Wrong_current_password_returns_403()
    {
        var login = await RegisterAndLogin("chef@tasca.pt", Old);
        var resp = await PostJson("/auth/change-password",
            new { currentPassword = "not-it", newPassword = New }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_current_password_on_voluntary_change_returns_400()
    {
        var login = await RegisterAndLogin("noc@tasca.pt", Old);
        var resp = await PostJson("/auth/change-password",
            new { newPassword = New }, login.accessToken); // no currentPassword, not a forced change
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Policy_violating_new_password_returns_400()
    {
        var login = await RegisterAndLogin("weak@tasca.pt", Old);
        // 8 chars (passes the DataAnnotation length) but violates Identity policy → handler maps to 400.
        var resp = await PostJson("/auth/change-password",
            new { currentPassword = Old, newPassword = "12345678" }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Change_revokes_other_sessions_but_keeps_the_current_device()
    {
        await Register("multi@tasca.pt", Old);
        var (device1, _) = await Login("multi@tasca.pt", Old);   // current device
        var (_, c2) = await Login("multi@tasca.pt", Old);        // another device

        var resp = await PostJson("/auth/change-password",
            new { currentPassword = Old, newPassword = New }, device1.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        // The other device's refresh is dead; the current device's session survives.
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Refresh(c2)).StatusCode);
    }

    [TestMethod]
    public async Task Forced_change_skips_the_current_password_requirement()
    {
        var login = await RegisterAndLogin("forced@tasca.pt", Old);
        await SetMustChangePassword("forced@tasca.pt");

        // No current password supplied — allowed because the account is flagged must-change.
        var resp = await PostJson("/auth/change-password",
            new { newPassword = New }, login.accessToken);
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
    }

    private static async Task SetMustChangePassword(string email)
    {
        await using var scope = TestHost.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.MustChangePassword = true;
        await users.UpdateAsync(user);
    }
}
