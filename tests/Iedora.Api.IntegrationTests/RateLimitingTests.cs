using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

[TestClass]
public sealed class RateLimitingTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Sensitive_auth_endpoints_return_429_past_the_limit()
    {
        // Spin a host with a tiny auth limit (the shared factory raises limits so the suite isn't
        // throttled). Its rate-limiter state is fresh + isolated from the other tests' host.
        using var factory = TestHost.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "3"));
        var client = factory.CreateClient();

        // Bad credentials → 401 while under the limit, then 429 once the window fills.
        HttpStatusCode last = HttpStatusCode.OK;
        for (var i = 0; i < 6; i++)
            last = (await client.PostAsJsonAsync("/auth/login", new { email = "nobody@x.pt", password = "wrong-password" })).StatusCode;

        Assert.AreEqual(HttpStatusCode.TooManyRequests, last);
    }

    [TestMethod]
    public async Task Non_sensitive_reads_are_not_throttled_by_the_auth_limit()
    {
        // The public menu render is NOT in the sensitive-auth set, so even a tiny auth limit leaves it alone.
        using var factory = TestHost.Factory.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitLimit", "1"));
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            var resp = await client.GetAsync("/public/r/does-not-exist"); // 404, never 429
            Assert.AreNotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }
    }
}
