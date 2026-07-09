using Framework.Email;
using Framework.Outbox;
using Iedora.Identity;
using Framework.Notifications;
using Iedora.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
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
        builder.UseSetting("ServiceToken:Clients:test-client", "test-secret"); // client-credentials grant
        // Raise rate limits far above anything the suite does, so shared-IP test traffic isn't
        // throttled (RateLimitingTests lowers them per-host to assert the 429 path).
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000000");
        builder.UseSetting("RateLimiting:UploadPermitLimit", "1000000");
        builder.UseSetting("RateLimiting:GlobalPermitLimit", "1000000");

        builder.ConfigureTestServices(services =>
        {
            services.Configure<OutboxOptions>(o => { o.PollSeconds = 3600; o.WakeOnNotify = false; }); // tests dispatch directly
            services.AddIdentityMessagingHandlers(); // Identity outbox + inbox + handlers (create-user saga)
            services.AddTenancyHandlers();           // Tenancy outbox + inbox + handlers (create-tenant, transfer saga)
            // The Notifications service (its own inbox + SMTP delivery) isn't referenced by the API,
            // so register its DbContext + handlers here to drive email end-to-end in-process.
            services.AddDbContext<NotificationsDbContext>(o => o.UseNpgsql(connectionString,
                p => p.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));
            services.AddNotificationsHandlers();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>()); // override the real sender
        });
    }
}
