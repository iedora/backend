using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Iedora.Auth.E2ETests;

// True e2e: boot the whole Aspire AppHost (a real Postgres container + the auth service, wired
// exactly as it deploys) and assert the orchestration converges — Postgres starts, the auth
// service connects, applies EF migrations, and reports healthy. This is the tip of the test
// diamond: HTTP behavior (register/login/refresh/...) is covered exhaustively by the
// integration suite against a real database; here we prove the AppHost wiring itself works.
public sealed class AuthAppHostTests
{
    [Fact]
    public async Task AppHost_boots_postgres_and_a_healthy_auth_service()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Iedora_AppHost>();
        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        // The auth service only reaches Healthy after it has connected to Postgres and its
        // hosted migration step has completed — so this single wait exercises the whole chain.
        var healthy = await app.ResourceNotifications
            .WaitForResourceHealthyAsync("auth")
            .WaitAsync(TimeSpan.FromMinutes(3));

        Assert.Equal("auth", healthy.Resource.Name);
    }
}
