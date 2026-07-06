using Framework.Outbox;
using Iedora.Data;
using Iedora.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Api.IntegrationTests;

/// <summary>
/// In-process host pointed at the shared Testcontainers Postgres. Secure cookies are disabled so
/// the test client (plain HTTP) round-trips the refresh cookie. In production the outbox is
/// drained by the dedicated Iedora.Api.Worker; here we register the dispatcher + a FakeEmailSender
/// into the API host so integration tests can drive the outbox end-to-end (TestHost dispatches it
/// deterministically). The container lifecycle + migrations + Respawn live in <see cref="TestHost"/>.
/// </summary>
public sealed class AuthApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:authdb", connectionString);
        builder.UseSetting("Session:CookieSecure", "false");

        builder.ConfigureTestServices(services =>
        {
            services.Configure<OutboxOptions>(o => o.PollSeconds = 3600); // tests dispatch directly
            services.AddOutbox<IdentityDbContext>();
            services.AddScoped<IOutboxHandler, PasswordResetEmailHandler>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());
        });
    }
}
