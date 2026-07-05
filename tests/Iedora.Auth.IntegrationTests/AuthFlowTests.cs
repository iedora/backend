using System.Net;
using System.Net.Http.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Auth.IntegrationTests;

[TestClass]
public sealed class AuthFlowTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Register_creates_account_and_returns_201()
    {
        var resp = await Register("owner@tasca.pt", "Sup3rSecret!");
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    [TestMethod]
    public async Task Register_duplicate_email_returns_400()
    {
        await Register("dupe@tasca.pt", "Sup3rSecret!");
        var second = await Register("dupe@tasca.pt", "Sup3rSecret!");
        Assert.AreEqual(HttpStatusCode.BadRequest, second.StatusCode);
    }

    [TestMethod]
    [DataRow("not-an-email", "Sup3rSecret!")] // invalid email → built-in validation 400
    [DataRow("shortpw@tasca.pt", "short")]     // too-short password → 400
    public async Task Register_invalid_input_returns_400(string email, string password)
    {
        var resp = await Register(email, password);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Login_with_correct_password_issues_token_and_sets_refresh_cookie()
    {
        await Register("chef@bistro.pt", "Sup3rSecret!");
        var resp = await Client.PostAsJsonAsync("/auth/login", new { email = "chef@bistro.pt", password = "Sup3rSecret!" });

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TokenPayload>())!;
        Assert.IsNotEmpty(body.accessToken);
        Assert.IsNotEmpty(body.userId);
        Assert.IsNotNull(RefreshCookieFrom(resp)); // HttpOnly refresh cookie present
    }

    [TestMethod]
    public async Task Login_with_wrong_password_returns_401()
    {
        await Register("chef@bistro.pt", "Sup3rSecret!");
        var resp = await Client.PostAsJsonAsync("/auth/login", new { email = "chef@bistro.pt", password = "wrong" });
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Whoami_without_token_returns_401()
    {
        var resp = await Get("/auth/whoami");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Whoami_with_token_returns_identity()
    {
        var login = await RegisterAndLogin("me@tasca.pt", "Sup3rSecret!");
        var resp = await Get("/auth/whoami", login.accessToken);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var who = (await resp.Content.ReadFromJsonAsync<WhoAmiPayload>())!;
        Assert.AreEqual(login.userId, who.userId);
        Assert.AreEqual("me@tasca.pt", who.email);
    }

    [TestMethod]
    public async Task Jwks_exposes_es256_public_key()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(
            await Client.GetStringAsync("/auth/.well-known/jwks.json"));
        var key = doc.RootElement.GetProperty("keys")[0];
        Assert.AreEqual("EC", key.GetProperty("kty").GetString());
        Assert.AreEqual("ES256", key.GetProperty("alg").GetString());
        Assert.IsFalse(key.TryGetProperty("d", out _)); // never leak the private scalar
    }
}
