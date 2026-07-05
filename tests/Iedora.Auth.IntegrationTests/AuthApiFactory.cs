using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Iedora.Auth.IntegrationTests;

/// <summary>
/// In-process host pointed at the shared Testcontainers Postgres. Secure cookies are disabled
/// so the test client (plain HTTP) round-trips the refresh cookie. The container lifecycle +
/// migrations + Respawn live in <see cref="TestHost"/> (assembly-level).
/// </summary>
public sealed class AuthApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:authdb", connectionString);
        builder.UseSetting("Session:CookieSecure", "false");
    }
}
