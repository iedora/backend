using System.Data.Common;
using Iedora.Auth.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace Iedora.Auth.IntegrationTests;

/// <summary>
/// The integration harness: one real Postgres (Testcontainers) behind an in-process host
/// (WebApplicationFactory). The app's own startup migrates the schema; Respawn truncates
/// data between tests so the shared container stays fast. Secure cookies are disabled so the
/// test client (plain HTTP) round-trips the refresh cookie.
/// </summary>
public sealed class AuthApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgres:18-alpine").Build();

    private Respawner _respawner = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:authdb", _db.GetConnectionString());
        builder.UseSetting("Session:CookieSecure", "false"); // test client speaks HTTP
    }

    public async ValueTask InitializeAsync()
    {
        await _db.StartAsync();

        // Force the host to build + run its hosted services → EF migrations apply now.
        await using (var scope = Services.CreateAsyncScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await ctx.Database.MigrateAsync();
        }

        await using var conn = await OpenConnectionAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
        });
    }

    /// <summary>Truncate all data (keeps schema + migration history). Call before each test.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await _respawner.ResetAsync(conn);
    }

    private async Task<DbConnection> OpenConnectionAsync()
    {
        // Reuse the EF provider's connection type without a direct Npgsql reference.
        await using var scope = Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var conn = (DbConnection)Activator.CreateInstance(ctx.Database.GetDbConnection().GetType())!;
        conn.ConnectionString = _db.GetConnectionString();
        await conn.OpenAsync();
        return conn;
    }

    public override async ValueTask DisposeAsync()
    {
        await _db.DisposeAsync();
        await base.DisposeAsync();
    }
}
