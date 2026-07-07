using Framework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Iedora.Identity;

/// <summary>Wires the Identity module's <see cref="IdentityDbContext"/> onto the shared <c>authdb</c>
/// Postgres via the Aspire Npgsql integration, pinning its migrations-history table into the
/// <c>identity</c> schema and enabling outbox NOTIFY wake-ups. Used by the API, the migration worker,
/// and the outbox worker.</summary>
public static class IdentityDataRegistration
{
    public static IHostApplicationBuilder AddIdentityDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<IdentityDbContext>("authdb",
            configureDbContextOptions: o => o
                .UseNpgsql(p => p.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
                .UseOutboxNotifications());
        return builder;
    }
}
