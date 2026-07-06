using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.E2ETests;

// True e2e: boot the whole Aspire AppHost (a real Postgres container + the auth service, wired
// exactly as it deploys) and assert the orchestration converges — Postgres starts, the auth
// service connects, applies EF migrations, and reports healthy. This is the tip of the test
// diamond: HTTP behavior (register/login/refresh/...) is covered exhaustively by the
// integration suite against a real database; here we prove the AppHost wiring itself works.
[TestClass]
public sealed class AuthAppHostTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task AppHost_boots_postgres_and_a_healthy_auth_service()
    {
        var ct = TestContext.CancellationTokenSource.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Iedora_AppHost>();
        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);

        // The auth service only reaches Healthy after it has connected to Postgres and its
        // hosted migration step has completed — so this single wait exercises the whole chain.
        var healthy = await app.ResourceNotifications
            .WaitForResourceHealthyAsync("api", ct)
            .WaitAsync(TimeSpan.FromMinutes(3), ct);

        Assert.AreEqual("api", healthy.Resource.Name);
    }
}
