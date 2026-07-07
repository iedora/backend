using Framework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Iedora.Tenancy;

/// <summary>Wires the Tenancy module's <see cref="TenancyDbContext"/> onto the shared <c>authdb</c>
/// Postgres (Aspire Npgsql), pinning its migrations-history table into the <c>tenancy</c> schema and
/// enabling outbox NOTIFY wake-ups.</summary>
public static class TenancyDataRegistration
{
    public static IHostApplicationBuilder AddTenancyDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<TenancyDbContext>("authdb",
            configureDbContextOptions: o => o
                .UseNpgsql(p => p.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDbContext.Schema))
                .UseOutboxNotifications());
        return builder;
    }
}
