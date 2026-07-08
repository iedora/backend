using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Framework.Notifications;

/// <summary>Wires the Notifications service's <see cref="NotificationsDbContext"/> onto the shared
/// <c>authdb</c> Postgres (Aspire Npgsql), pinning its migrations-history table into the
/// <c>notifications</c> schema.</summary>
public static class NotificationsDataRegistration
{
    public static IHostApplicationBuilder AddNotificationsDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<NotificationsDbContext>("authdb",
            configureDbContextOptions: o => o
                .UseNpgsql(p => p.MigrationsHistoryTable("__EFMigrationsHistory", NotificationsDbContext.Schema)));
        return builder;
    }
}
