using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class TokenRefreshTests
{
    private static readonly DateTimeOffset Expires = new(2026, 7, 9, 12, 0, 0, TimeSpan.Zero);

    private sealed class Stub(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(response);
    }

    private static AuthApi StubApi(string access, string refresh)
    {
        var resp = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new { accessToken = access, expiresAt = Expires.AddHours(1).ToString("o"), userId = "u" }),
        };
        resp.Headers.TryAddWithoutValidation("Set-Cookie", $"iedora_refresh={refresh}; path=/auth");
        var http = new HttpClient(new Stub(resp)) { BaseAddress = new Uri("http://api") };
        return new AuthApi(http, Options.Create(new ApiAuthOptions()));
    }

    private static CookieValidatePrincipalContext Context(FakeTimeProvider clock, AuthApi? api)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        if (api is not null) services.AddSingleton(api);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };

        var claims = TokenRefresh.BuildClaims("user-1", "a@b.pt", ["admin"], new AuthResult("acc-old", Expires, "ref-old"));
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, "Cookies")), "Cookies");
        var scheme = new AuthenticationScheme("Cookies", null, typeof(CookieAuthenticationHandler));
        return new CookieValidatePrincipalContext(http, scheme, new CookieAuthenticationOptions(), ticket);
    }

    [TestMethod]
    public void BuildClaims_carries_the_tokens_expiry_and_roles()
    {
        var claims = TokenRefresh.BuildClaims("user-1", "a@b.pt", ["admin", "staff"], new AuthResult("acc", Expires, "ref"));

        Assert.AreEqual("acc", claims.Single(c => c.Type == AccessToken.ClaimType).Value);
        Assert.AreEqual("ref", claims.Single(c => c.Type == AccessToken.RefreshClaimType).Value);
        Assert.AreEqual(Expires.ToString("o"), claims.Single(c => c.Type == AccessToken.ExpiresClaimType).Value);
        CollectionAssert.AreEquivalent(new[] { "admin", "staff" },
            claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList());
    }

    [TestMethod]
    public async Task Leaves_a_still_fresh_token_untouched()
    {
        var ctx = Context(new FakeTimeProvider(Expires.AddMinutes(-30)), api: null); // well before expiry

        await TokenRefresh.OnValidatePrincipalAsync(ctx);

        Assert.IsFalse(ctx.ShouldRenew);
        Assert.AreEqual("acc-old", ctx.Principal!.FindFirst(AccessToken.ClaimType)!.Value);
    }

    [TestMethod]
    public async Task Rotates_the_token_when_near_expiry()
    {
        var ctx = Context(new FakeTimeProvider(Expires), StubApi("acc-new", "ref-new")); // inside the skew window

        await TokenRefresh.OnValidatePrincipalAsync(ctx);

        Assert.IsTrue(ctx.ShouldRenew);
        Assert.AreEqual("acc-new", ctx.Principal!.FindFirst(AccessToken.ClaimType)!.Value);
        Assert.AreEqual("ref-new", ctx.Principal.FindFirst(AccessToken.RefreshClaimType)!.Value);
    }
}
