using System.Diagnostics;
using Iedora.Data;
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
        using var activity = _activitySource.StartActivity("migrate auth schema", ActivityKind.Client);
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(() => db.Database.MigrateAsync(stoppingToken));

            logger.LogInformation("Auth schema is up to date.");
        }
        catch (Exception ex)
        {
            activity?.AddException(ex);
            throw;
        }

        lifetime.StopApplication();
    }
}
