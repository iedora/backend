using Iedora.Auth.Data;
using Framework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Iedora.Auth.IntegrationTests;

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
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await ctx.Database.MigrateAsync(ct);
        }

        _conn = new NpgsqlConnection(_db.GetConnectionString());
        await _conn.OpenAsync(ct);
        _respawner = await Respawner.CreateAsync(_conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")],
        });
    }

    /// <summary>Truncate all data (keeps schema + migration history). Runs before each test.</summary>
    public static Task ResetAsync() => _respawner.ResetAsync(_conn);

    /// <summary>The captured emails (fake sender). Cleared per test by the base class.</summary>
    public static FakeEmailSender EmailSender => Factory.Services.GetRequiredService<FakeEmailSender>();

    /// <summary>Dispatch the outbox once, deterministically (the background poller is parked in tests).</summary>
    public static async Task<int> DispatchOutboxAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor<AuthDbContext>>().DispatchPendingAsync(CancellationToken.None);
    }

    [AssemblyCleanup]
    public static async Task Cleanup()
    {
        await _conn.DisposeAsync();
        Factory.Dispose();
        await _db.DisposeAsync();
    }
}
