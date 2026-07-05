using Microsoft.EntityFrameworkCore;

namespace Iedora.Auth.Data;

// Applies EF Core migrations on startup. Runs as a hosted service — i.e. ONLY when the server
// actually starts — so the build-time OpenAPI generator (which builds the host but never runs
// it) never reaches for Postgres. Retries: Postgres may still be finishing startup.
public sealed class SchemaInitializer(IServiceProvider services, ILogger<SchemaInitializer> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await db.Database.MigrateAsync(cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < 12)
            {
                logger.LogWarning("DB not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(2000, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
