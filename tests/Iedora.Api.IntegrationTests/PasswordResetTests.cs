using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class PasswordResetTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Forgot_password_for_existing_user_enqueues_and_sends_a_reset_email()
    {
        await RegisterAccount("owner@tasca.pt", "Sup3rSecret!");
        var resp = await PostJson("/auth/forgot-password", new { email = "owner@tasca.pt" });
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        // The email is queued in the outbox; dispatch it and check it went out.
        Assert.AreEqual(1, await TestHost.DispatchOutboxAsync());
        var email = TestHost.EmailSender.Sent.Single();
        Assert.AreEqual("owner@tasca.pt", email.To);
        Assert.IsTrue(email.HtmlBody.Contains("reset", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Forgot_password_for_unknown_user_returns_200_without_sending()
    {
        var resp = await PostJson("/auth/forgot-password", new { email = "nobody@tasca.pt" });
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);   // no account enumeration
        Assert.AreEqual(0, await TestHost.DispatchOutboxAsync());
        Assert.IsEmpty(TestHost.EmailSender.Sent);
    }

    [TestMethod]
    public async Task Reset_password_with_a_valid_token_lets_the_new_password_log_in()
    {
        await RegisterAccount("chef@tasca.pt", "Sup3rSecret!");
        await PostJson("/auth/forgot-password", new { email = "chef@tasca.pt" });
        await TestHost.DispatchOutboxAsync();
        var (email, token) = ExtractResetLink(TestHost.EmailSender.Sent.Single().HtmlBody);

        var reset = await PostJson("/auth/reset-password", new { email, token, password = "Ev3nBetter!" });
        Assert.AreEqual(HttpStatusCode.NoContent, reset.StatusCode);

        // Old password rejected; the reset one works.
        var oldTry = await Client.PostAsJsonAsync("/auth/login", new { email = "chef@tasca.pt", password = "Sup3rSecret!" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, oldTry.StatusCode);
        var newTry = await Client.PostAsJsonAsync("/auth/login", new { email = "chef@tasca.pt", password = "Ev3nBetter!" });
        Assert.AreEqual(HttpStatusCode.OK, newTry.StatusCode);
    }

    [TestMethod]
    public async Task Reset_password_with_a_bad_token_returns_400()
    {
        await RegisterAccount("bad@tasca.pt", "Sup3rSecret!");
        var reset = await PostJson("/auth/reset-password",
            new { email = "bad@tasca.pt", token = "not-a-real-token", password = "Ev3nBetter!" });
        Assert.AreEqual(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    // Pull the email + token back out of the reset link in the email body.
    private static (string email, string token) ExtractResetLink(string html)
    {
        var href = Regex.Match(html, "href=\"([^\"]+)\"").Groups[1].Value;
        var query = new Uri(href).Query.TrimStart('?');
        var parts = query.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        return (parts["email"], parts["token"]);
    }
}
