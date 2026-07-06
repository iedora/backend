using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Iedora.Data;

/// <summary>Design-time factory so <c>dotnet ef migrations add … --context IdentityDbContext</c>
/// can build the model without the app host or a live database (the connection string is a
/// placeholder). The migrations-history table lives in the module's own schema so the two module
/// contexts don't collide on one shared history table.</summary>
public sealed class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql("Host=localhost;Database=authdb;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", IdentityDbContext.Schema))
            .Options;
        return new IdentityDbContext(options);
    }
}
