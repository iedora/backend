using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Iedora.Menus;

/// <summary>Design-time factory for <c>dotnet ef migrations add … --context MenuDbContext</c>.
/// Its migrations-history table lives in the <c>menu</c> schema, so it never collides with the
/// other modules' contexts on a shared history table.</summary>
public sealed class MenuDbContextFactory : IDesignTimeDbContextFactory<MenuDbContext>
{
    public MenuDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MenuDbContext>()
            .UseNpgsql("Host=localhost;Database=authdb;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", MenuDbContext.Schema))
            .Options;
        return new MenuDbContext(options);
    }
}
