using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Framework.Outbox;

/// <summary>
/// Dispatches the outbox for one DbContext type. Woken by a Postgres <c>NOTIFY</c> the moment a
/// message is enqueued (near-zero latency), with a periodic poll kept as the safety net for any
/// notification missed while the listener was disconnected. The processor claims with
/// <c>FOR UPDATE SKIP LOCKED</c>, so this stays multi-replica-safe. Generic over the DbContext, so
/// one worker runs one of these per registered module.
/// </summary>
public sealed class OutboxBackgroundService<TContext>(
    IServiceScopeFactory scopes, IOptions<OutboxOptions> options,
    ILogger<OutboxBackgroundService<TContext>> logger) : BackgroundService
    where TContext : DbContext
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        var pollInterval = TimeSpan.FromSeconds(opt.PollSeconds);

        // The listener target (connection + channel), read once from a scoped context.
        string? connectionString;
        string channel;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            connectionString = db.Database.GetConnectionString();
            channel = OutboxChannel.For(db);
        }

        // A NOTIFY (or the poll timeout) signals the dispatch loop; coalesced to a single pending tick.
        var wake = Channel.CreateBounded<byte>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
        var listener = opt.WakeOnNotify && connectionString is not null
            ? ListenAsync(connectionString, channel, wake.Writer, ct)
            : Task.CompletedTask;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                await DrainAsync(ct);

                // Wait for a NOTIFY wake-up, or the poll interval — whichever comes first.
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
                wait.CancelAfter(pollInterval);
                try { await wake.Reader.ReadAsync(wait.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { } // poll fallback fired
            }
        }
        catch (OperationCanceledException) { }
        finally { await listener; }
    }

    private async Task DrainAsync(CancellationToken ct)
    {
        try
        {
            // Drain successes so a burst larger than one batch doesn't wait for the next signal.
            while (!ct.IsCancellationRequested)
            {
                await using var scope = scopes.CreateAsyncScope();
                var handled = await scope.ServiceProvider
                    .GetRequiredService<OutboxProcessor<TContext>>().DispatchPendingAsync(ct);
                if (handled == 0) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "Outbox dispatch error for {Context}.", typeof(TContext).Name);
        }
    }

    private async Task ListenAsync(string connectionString, string channel, ChannelWriter<byte> wake, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(ct);
                conn.Notification += (_, _) => wake.TryWrite(0);
                await using (var cmd = new NpgsqlCommand($"LISTEN {channel}", conn))
                    await cmd.ExecuteNonQueryAsync(ct);

                wake.TryWrite(0); // catch-up: dispatch once after (re)connect, covering any missed notify
                while (!ct.IsCancellationRequested)
                    await conn.WaitAsync(ct); // blocks until a notification arrives
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox listener on {Channel} dropped; reconnecting.", channel);
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
