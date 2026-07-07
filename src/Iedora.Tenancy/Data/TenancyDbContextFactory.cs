using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Iedora.Tenancy;

/// <summary>Design-time factory for <c>dotnet ef migrations add … --context TenancyDbContext</c>.
/// Its migrations-history table lives in the <c>tenancy</c> schema (see
/// <see cref="IdentityDbContextFactory"/> for the rationale).</summary>
public sealed class TenancyDbContextFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql("Host=localhost;Database=authdb;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable("__EFMigrationsHistory", TenancyDbContext.Schema))
            .Options;
        return new TenancyDbContext(options);
    }
}
