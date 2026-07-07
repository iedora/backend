using System.Diagnostics;
using Iedora.Identity;
using Iedora.Menus;
using Iedora.Notifications;
using Iedora.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Iedora.MigrationService;

/// <summary>
/// Applies EF Core migrations once, then stops the host (exits 0). The AppHost gates the auth
/// API on this worker's completion (<c>WaitForCompletion</c>), so the API never races on the
/// schema. The migration runs inside the retrying execution strategy the Aspire Npgsql
/// integration installs — the canonical Aspire migration-worker pattern.
/// </summary>
public sealed class MigrationWorker(
    IServiceProvider services,
    IHostApplicationLifetime lifetime,
    IHostEnvironment environment,
    ILogger<MigrationWorker> logger) : BackgroundService
{
    private readonly ActivitySource _activitySource = new(environment.ApplicationName);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var activity = _activitySource.StartActivity("migrate database schema", ActivityKind.Client);
        try
        {
            await using var scope = services.CreateAsyncScope();
            // Each module owns a schema + its own migrations; this worker applies them all.
            await MigrateAsync<IdentityDbContext>(scope.ServiceProvider, stoppingToken);
            await MigrateAsync<TenancyDbContext>(scope.ServiceProvider, stoppingToken);
            await MigrateAsync<MenuDbContext>(scope.ServiceProvider, stoppingToken);
            await MigrateAsync<NotificationsDbContext>(scope.ServiceProvider, stoppingToken);

            logger.LogInformation("Database schema is up to date.");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        lifetime.StopApplication();
    }

    private static async Task MigrateAsync<TContext>(IServiceProvider scope, CancellationToken ct)
        where TContext : DbContext
    {
        var db = scope.GetRequiredService<TContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(() => db.Database.MigrateAsync(ct));
    }
}
