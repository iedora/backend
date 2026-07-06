using Framework.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Framework.Commands;

public static class CommandExtensions
{
    /// <summary>Map the command-tracking table. Call from the consuming DbContext's
    /// <c>OnModelCreating</c> (alongside <c>MapOutbox</c>).</summary>
    public static ModelBuilder MapCommands(this ModelBuilder builder)
    {
        builder.Entity<Command>(command =>
        {
            command.ToTable("commands");
            command.HasKey(c => c.Id);
            command.Property(c => c.Status).HasConversion<string>();
        });
        return builder;
    }

    /// <summary>
    /// Stage a <c>Pending</c> command AND its outbox message on the DbContext — both committed by
    /// the caller's <c>SaveChangesAsync</c>, in ONE transaction. The accept endpoint calls this
    /// after synchronous validation, then returns 202 with <paramref name="commandId"/>. That id is
    /// the client's idempotency key: re-submitting it conflicts on the command PK, so a duplicate
    /// accept is a no-op the caller can turn into "here's your existing status".
    /// </summary>
    public static Command SubmitCommand<T>(this DbContext db, Guid commandId, string type, T payload, TimeProvider clock)
    {
        var command = new Command { Id = commandId, Type = type, CreatedAt = clock.GetUtcNow() };
        db.Set<Command>().Add(command);
        db.EnqueueOutbox(type, new CommandEnvelope<T>(commandId, payload), clock);
        return command;
    }

    /// <summary>The command's current tracking row, or null if unknown.</summary>
    public static ValueTask<Command?> FindCommandAsync(this DbContext db, Guid id, CancellationToken ct) =>
        db.Set<Command>().FindAsync([id], ct);
}
