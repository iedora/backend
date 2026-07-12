using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class ApiAuthStateProviderTests
{
    // A /auth/refresh handler gated on a Task, so a refresh can be held "in flight" while other auth
    // checks arrive; counts how many refreshes were issued.
    private sealed class GatedRefresh(Task gate, string? token) : HttpMessageHandler
    {
        public int Refreshes { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath != "/auth/refresh") return new(HttpStatusCode.NotFound);
            Refreshes++;
            await gate;
            return token is null ? new(HttpStatusCode.Unauthorized) : TestHttp.Token(token);
        }
    }

    private static ApiAuthStateProvider Provider(GatedRefresh handler) =>
        new(new TokenStore(), new ApiAuthClient(new TestHttp.Factory(handler)));

    [TestMethod]
    public async Task Auth_checks_during_an_inflight_refresh_share_it_and_all_see_the_result()
    {
        var gate = new TaskCompletionSource();
        var handler = new GatedRefresh(gate.Task, "acc-1");
        var provider = Provider(handler);

        // Three checks race on first load (router + layout + page) while the refresh is still in flight.
        var checks = new[]
        {
            provider.GetAuthenticationStateAsync(),
            provider.GetAuthenticationStateAsync(),
            provider.GetAuthenticationStateAsync(),
        };
        gate.SetResult(); // let the single refresh complete
        var states = await Task.WhenAll(checks);

        Assert.AreEqual(1, handler.Refreshes, "the silent refresh must fire once, not per caller");
        Assert.IsTrue(states.All(s => s.User.Identity?.IsAuthenticated == true),
            "no caller should see Anonymous while the shared refresh is still resolving");
    }

    [TestMethod]
    public async Task A_failed_silent_refresh_is_anonymous_and_never_retried()
    {
        var handler = new GatedRefresh(Task.CompletedTask, token: null); // 401 immediately
        var provider = Provider(handler);

        Assert.IsFalse((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated ?? false);
        Assert.IsFalse((await provider.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated ?? false);
        Assert.AreEqual(1, handler.Refreshes, "a failed refresh must not retry on every auth check");
    }
}
