using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class AuthApiTests
{
    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    private static (AuthApi api, Stub stub) Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var stub = new Stub(respond);
        var http = new HttpClient(stub) { BaseAddress = new Uri("http://api") };
        return (new AuthApi(http, Options.Create(new ApiAuthOptions())), stub);
    }

    // Mirrors the API's /auth/login|refresh response: TokenResponse body + a rotated refresh cookie.
    private static HttpResponseMessage TokenResponse(string access, string refreshCookie, DateTimeOffset expires)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accessToken = access, expiresAt = expires.ToString("o"), userId = "u" }),
        };
        resp.Headers.TryAddWithoutValidation("Set-Cookie", $"iedora_refresh={refreshCookie}; path=/auth; httponly");
        return resp;
    }

    [TestMethod]
    public async Task Login_parses_the_token_and_captures_the_refresh_cookie()
    {
        var expires = new DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);
        var (api, _) = Build(_ => TokenResponse("acc-1", "ref-1", expires));

        var result = await api.LoginAsync("a@b.pt", "pw", CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.AreEqual("acc-1", result!.AccessToken);
        Assert.AreEqual("ref-1", result.RefreshToken);
        Assert.AreEqual(expires, result.ExpiresAt);
    }

    [TestMethod]
    public async Task Refresh_replays_the_stored_cookie_and_captures_the_rotated_one()
    {
        var (api, stub) = Build(_ => TokenResponse("acc-2", "ref-2", DateTimeOffset.UtcNow.AddHours(1)));

        var result = await api.RefreshAsync("ref-1", CancellationToken.None);

        Assert.AreEqual("acc-2", result!.AccessToken);
        Assert.AreEqual("ref-2", result.RefreshToken); // rotated value replaces the old one
        Assert.IsTrue(stub.Last!.Headers.GetValues("Cookie").Single().Contains("iedora_refresh=ref-1"),
            "should have replayed the stored refresh cookie");
    }

    [TestMethod]
    public async Task A_failed_auth_returns_null()
    {
        var (api, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        Assert.IsNull(await api.LoginAsync("a@b.pt", "bad", CancellationToken.None));
    }
}
