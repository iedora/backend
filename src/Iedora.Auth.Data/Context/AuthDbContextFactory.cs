using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Iedora.Auth.Data;

/// <summary>
/// Design-time factory so `dotnet ef migrations add ...` can build the model WITHOUT the
/// app host or a live database (the connection string here is a placeholder — migrations
/// scaffold from the model, they don't connect).
/// </summary>
public sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseNpgsql("Host=localhost;Database=authdb;Username=postgres;Password=postgres")
            .Options;
        return new AuthDbContext(options);
    }
}
