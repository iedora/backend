using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Outbox;

/// <summary>
/// Polls the outbox for one DbContext type and dispatches pending messages. Multi-replica-safe
/// (the processor claims with <c>FOR UPDATE SKIP LOCKED</c>). Generic over the DbContext, so a
/// single worker runs one of these per registered service. Uses a TimeProvider-driven timer that
/// waits BEFORE the first tick (so tests that set a long interval never dispatch behind their backs).
/// </summary>
public sealed class OutboxBackgroundService<TContext>(
    IServiceScopeFactory scopes, TimeProvider clock,
    IOptions<OutboxOptions> options, ILogger<OutboxBackgroundService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.PollSeconds), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<OutboxProcessor<TContext>>().DispatchPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch loop error for {Context}.", typeof(TContext).Name);
            }
        }
    }
}
