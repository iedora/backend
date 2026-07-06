using Microsoft.Extensions.Options;

namespace Iedora.Auth.Outbox;

/// <summary>
/// Polls the outbox and dispatches pending messages. Single-instance dispatcher — fine for one
/// host; for multiple replicas add <c>FOR UPDATE SKIP LOCKED</c> or a leader so a message isn't
/// sent twice. Uses a TimeProvider-driven timer that waits BEFORE the first tick (so tests that
/// set a long interval never dispatch behind their backs).
/// </summary>
public sealed class OutboxBackgroundService(
    IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<OutboxOptions> options, ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch loop error.");
            }
        }
    }
}
