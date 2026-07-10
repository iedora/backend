using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class BearerHandlerTests
{
    // Inner handler that returns a scripted status sequence, recording the bearer sent each time.
    private sealed class Inner(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);
        public List<string?> Bearers { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Bearers.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(_statuses.Dequeue()));
        }
    }

    private static (BearerHandler handler, Inner inner, TokenStore tokens) Build(
        string initialToken, string? refreshed, params HttpStatusCode[] responses)
    {
        var tokens = new TokenStore { AccessToken = initialToken };
        var auth = TestHttp.AuthClient(refreshed);
        var state = new ApiAuthStateProvider(tokens, auth);
        var inner = new Inner(responses);
        return (new BearerHandler(tokens, auth, state) { InnerHandler = inner }, inner, tokens);
    }

    private static Task<HttpResponseMessage> CallAsync(BearerHandler handler) =>
        new HttpMessageInvoker(handler).SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://api/staff/overview"), CancellationToken.None);

    [TestMethod]
    public async Task Attaches_the_current_token()
    {
        var (handler, inner, _) = Build("acc-1", refreshed: "acc-2", HttpStatusCode.OK);

        await CallAsync(handler);

        CollectionAssert.AreEqual(new[] { "acc-1" }, inner.Bearers);
    }

    [TestMethod]
    public async Task On_401_it_refreshes_and_retries_with_the_rotated_token()
    {
        var (handler, inner, tokens) = Build("acc-1", refreshed: "acc-2", HttpStatusCode.Unauthorized, HttpStatusCode.OK);

        var response = await CallAsync(handler);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        CollectionAssert.AreEqual(new[] { "acc-1", "acc-2" }, inner.Bearers); // retried with the rotated token
        Assert.AreEqual("acc-2", tokens.AccessToken);
    }

    [TestMethod]
    public async Task On_401_with_a_failed_refresh_it_signs_out()
    {
        var (handler, _, tokens) = Build("acc-1", refreshed: null, HttpStatusCode.Unauthorized, HttpStatusCode.Unauthorized);

        var response = await CallAsync(handler);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.IsNull(tokens.AccessToken); // signed out
    }
}
