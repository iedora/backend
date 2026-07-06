using System.Text.Json;
using ErrorOr;
using Framework.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Framework.Commands;

/// <summary>
/// Base for a command's handler (it IS an <see cref="IOutboxHandler"/>, so the outbox worker runs
/// it): parse the envelope, do the domain work in <see cref="ExecuteAsync"/>, and record the
/// outcome on the command row. The work + the status flip commit together (same DbContext / same
/// outbox transaction). Idempotent: a command that's already terminal is skipped, so at-least-once
/// redelivery is safe. Expected failures come back as an <see cref="Error"/> and mark the command
/// <c>Failed</c> (terminal); throwing signals a transient failure, so the outbox retries.
/// </summary>
public abstract class CommandHandler<TContext, TPayload>(TContext db, TimeProvider clock) : IOutboxHandler
    where TContext : DbContext
{
    protected TContext Db { get; } = db;
    protected TimeProvider Clock { get; } = clock;

    public abstract string Type { get; }

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var envelope = JsonSerializer.Deserialize<CommandEnvelope<TPayload>>(payload)
            ?? throw new InvalidOperationException($"Command payload for '{Type}' was empty.");

        var command = await Db.Set<Command>().FindAsync([envelope.CommandId], ct);
        if (command is null || command.Status != CommandStatus.Pending)
            return; // unknown, or already terminal — idempotent no-op

        var result = await ExecuteAsync(envelope.Data, ct);
        if (result.IsError)
        {
            var error = result.Errors[0];
            command.Fail(error.Code, error.Description, Clock.GetUtcNow());
        }
        else
        {
            command.Succeed(result.Value, Clock.GetUtcNow());
        }
        await Db.SaveChangesAsync(ct);
    }

    /// <summary>Do the domain write (on <see cref="Db"/>). Return a result location (e.g. the created
    /// resource's path) on success, or an <see cref="Error"/> for an expected failure. Throw for a
    /// transient failure so the outbox retries.</summary>
    protected abstract Task<ErrorOr<string?>> ExecuteAsync(TPayload data, CancellationToken ct);
}
