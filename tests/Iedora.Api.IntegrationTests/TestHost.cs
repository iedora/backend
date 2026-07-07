using Iedora.Notifications;
using Iedora.Identity;
using Iedora.Menus;
using Iedora.Tenancy;
using Framework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Iedora.Api.IntegrationTests;

/// <summary>
/// One Postgres container + <see cref="AuthApiFactory"/> for the whole assembly, with Respawn
/// wiping data between tests. Assembly-level init is MSTest's equivalent of an xUnit collection
/// fixture — the expensive container is created once and shared by every test class.
/// </summary>
[TestClass]
public class TestHost
{
    private static PostgreSqlContainer _db = null!;
    private static NpgsqlConnection _conn = null!;
    private static Respawner _respawner = null!;

    public static AuthApiFactory Factory { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task Init(TestContext context)
    {
        var ct = context.CancellationTokenSource.Token;

        _db = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _db.StartAsync(ct);

        Factory = new AuthApiFactory(_db.GetConnectionString());
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            // Each module owns a schema + its own migrations — apply them all (as the worker does).
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<TenancyDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<MenuDbContext>().Database.MigrateAsync(ct);
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.MigrateAsync(ct);
        }

        _conn = new NpgsqlConnection(_db.GetConnectionString());
        await _conn.OpenAsync(ct);
        _respawner = await Respawner.CreateAsync(_conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["identity", "tenancy", "menu", "notifications"],
            TablesToIgnore =
            [
                new Respawn.Graph.Table("identity", "__EFMigrationsHistory"),
                new Respawn.Graph.Table("tenancy", "__EFMigrationsHistory"),
                new Respawn.Graph.Table("menu", "__EFMigrationsHistory"),
                new Respawn.Graph.Table("notifications", "__EFMigrationsHistory"),
            ],
        });
    }

    /// <summary>Truncate all data (keeps schema + migration history). Runs before each test.</summary>
    public static Task ResetAsync() => _respawner.ResetAsync(_conn);

    /// <summary>The captured emails (fake sender). Cleared per test by the base class.</summary>
    public static FakeEmailSender EmailSender => Factory.Services.GetRequiredService<FakeEmailSender>();

    /// <summary>Dispatch the Identity outbox once, deterministically (the poller is parked in tests).</summary>
    public static async Task<int> DispatchOutboxAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<IdentityDbContext>>().DispatchPendingAsync(CancellationToken.None);
    }

    /// <summary>Dispatch the Tenancy outbox once — runs the async-write command handlers (create-tenant, …).</summary>
    public static async Task<int> DispatchTenancyOutboxAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<TenancyDbContext>>().DispatchPendingAsync(CancellationToken.None);
    }

    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        await _conn.DisposeAsync();
        Factory.Dispose();
        await _db.DisposeAsync();
    }
}
