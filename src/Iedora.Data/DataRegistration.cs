using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Iedora.Data;

/// <summary>
/// One place that wires each module's DbContext onto the shared <c>authdb</c> Postgres via the
/// Aspire Npgsql integration (connection, health checks, DB telemetry) — and pins each context's
/// migrations-history table into its OWN schema so the two contexts never collide on a shared
/// history table. Used by the API, the migration worker, and the outbox worker (DRY).
/// </summary>
public static class DataRegistration
{
    public static IHostApplicationBuilder AddIdentityDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IdentityDbContext>("authdb",
            configureDbContextOptions: o => o.UseNpgsql(
                p => p.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema)));
        return builder;
    }

    public static IHostApplicationBuilder AddTenancyDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<TenancyDbContext>("authdb",
            configureDbContextOptions: o => o.UseNpgsql(
                p => p.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDbContext.Schema)));
        return builder;
    }
}
