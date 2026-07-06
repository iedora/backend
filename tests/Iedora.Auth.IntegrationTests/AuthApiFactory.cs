using Iedora.Auth.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iedora.Auth.IntegrationTests;

/// <summary>
/// In-process host pointed at the shared Testcontainers Postgres. Secure cookies are disabled so
/// the test client (plain HTTP) round-trips the refresh cookie; email goes to a FakeEmailSender;
/// the outbox background poll is pushed far out so tests dispatch it deterministically instead.
/// The container lifecycle + migrations + Respawn live in <see cref="TestHost"/> (assembly-level).
/// </summary>
public sealed class AuthApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:authdb", connectionString);
        builder.UseSetting("Session:CookieSecure", "false");
        builder.UseSetting("Outbox:PollSeconds", "3600"); // tests call the processor directly

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<FakeEmailSender>();
            services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FakeEmailSender>());
        });
    }
}
