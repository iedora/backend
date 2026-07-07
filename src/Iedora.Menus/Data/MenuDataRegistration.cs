using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace Iedora.Menus;

/// <summary>Wires the Menu module's <see cref="MenuDbContext"/> onto the shared <c>authdb</c> Postgres
/// (Aspire Npgsql), pinning its migrations-history table into the <c>menu</c> schema. No outbox yet.</summary>
public static class MenuDataRegistration
{
    public static IHostApplicationBuilder AddMenuDb(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDbContext<MenuDbContext>("authdb",
            configureDbContextOptions: o => o
                .UseNpgsql(p => p.MigrationsHistoryTable("__EFMigrationsHistory", MenuDbContext.Schema)));
        return builder;
    }
}
