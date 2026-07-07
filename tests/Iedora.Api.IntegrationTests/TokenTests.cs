using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire shape of the client-credentials grant response.
public sealed record ServiceTokenWire(string accessToken, string expiresAt, string tokenType);

[TestClass]
public sealed class TokenTests : IntegrationTestBase
{
    // Configured in AuthApiFactory: ServiceToken:Clients:test-client = test-secret.
    private const string ClientId = "test-client";
    private const string Secret = "test-secret";

    [TestMethod]
    public async Task Valid_client_credentials_mint_a_service_token()
    {
        var resp = await Token(ClientId, Secret);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

        var body = (await resp.Content.ReadFromJsonAsync<ServiceTokenWire>())!;
        Assert.AreEqual("Bearer", body.tokenType);
        Assert.IsNotEmpty(body.accessToken);

        // It's a service token: typ=service, sub=clientId, and no user roles.
        var claims = DecodeClaims(body.accessToken);
        Assert.AreEqual("service", claims.GetProperty("typ").GetString());
        Assert.AreEqual(ClientId, claims.GetProperty("sub").GetString());
        Assert.IsFalse(claims.TryGetProperty("roles", out _));
    }

    [TestMethod]
    public async Task Wrong_secret_is_rejected()
    {
        var resp = await Token(ClientId, "not-the-secret");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_client_is_rejected()
    {
        var resp = await Token("ghost-client", Secret);
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_or_non_basic_authorization_is_rejected()
    {
        Assert.AreEqual(HttpStatusCode.Unauthorized,
            (await Client.PostAsync("/auth/token", null)).StatusCode); // no header

        var bearer = new HttpRequestMessage(HttpMethod.Post, "/auth/token");
        bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "something");
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await Client.SendAsync(bearer)).StatusCode);
    }

    private Task<HttpResponseMessage> Token(string clientId, string secret)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/auth/token");
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        return Client.SendAsync(req);
    }

    private static JsonElement DecodeClaims(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=')
            .Replace('-', '+').Replace('_', '/');
        return JsonSerializer.Deserialize<JsonElement>(Convert.FromBase64String(padded));
    }
}
