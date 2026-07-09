using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class BearerHandlerTests
{
    // Captures the request the handler forwards, so we can inspect its Authorization header.
    private sealed class CapturingInner : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class FakeAuth(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }

    private static FakeAuth Anonymous() => new(new ClaimsPrincipal(new ClaimsIdentity()));

    private static async Task<HttpRequestMessage> ForwardAsync(AccessToken token, AuthenticationStateProvider auth)
    {
        var inner = new CapturingInner();
        var handler = new BearerHandler(token, auth) { InnerHandler = inner };
        using var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://api/staff/overview"), CancellationToken.None);
        return inner.Seen!;
    }

    [TestMethod]
    public async Task Attaches_the_login_seeded_token()
    {
        var req = await ForwardAsync(new AccessToken { Value = "tok-login" }, Anonymous());
        Assert.AreEqual("Bearer", req.Headers.Authorization?.Scheme);
        Assert.AreEqual("tok-login", req.Headers.Authorization?.Parameter);
    }

    [TestMethod]
    public async Task Falls_back_to_the_cookie_claim_when_not_seeded()
    {
        var identity = new ClaimsIdentity([new Claim(AccessToken.ClaimType, "tok-cookie")], "cookie");
        var req = await ForwardAsync(new AccessToken(), new FakeAuth(new ClaimsPrincipal(identity)));
        Assert.AreEqual("tok-cookie", req.Headers.Authorization?.Parameter);
    }

    [TestMethod]
    public async Task Sends_no_authorization_header_when_there_is_no_token()
    {
        var req = await ForwardAsync(new AccessToken(), Anonymous());
        Assert.IsNull(req.Headers.Authorization);
    }
}
