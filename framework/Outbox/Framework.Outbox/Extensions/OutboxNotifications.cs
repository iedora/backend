using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Framework.Outbox;

/// <summary>The Postgres LISTEN/NOTIFY channel an outbox uses to nudge its dispatcher awake — one
/// per schema, derived identically by the notifier and the listener.</summary>
public static class OutboxChannel
{
    public static string Name(string? schema) => "outbox_" + (schema ?? "public");
    public static string For(DbContext context) => Name(context.Model.GetDefaultSchema());
}

/// <summary>
/// A SaveChanges interceptor that fires a Postgres <c>NOTIFY</c> whenever outbox rows are inserted,
/// on the SAME connection as the write (so it's delivered when that transaction commits). The
/// payload is empty — the outbox table is the source of truth; NOTIFY is only a low-latency wake-up
/// hint, never the queue. Registered via <see cref="OutboxNotificationExtensions.UseOutboxNotifications"/>.
/// </summary>
public sealed class OutboxNotifyInterceptor : SaveChangesInterceptor
{
    // Per-context-instance flag (a shared singleton interceptor sees many contexts). SaveChanges on
    // one context is never concurrent, so the Saving→Saved pair is well-ordered.
    private readonly ConditionalWeakTable<DbContext, object> _hasOutboxWrite = new();
    private static readonly object Marker = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is { } context &&
            context.ChangeTracker.Entries<OutboxMessage>().Any(e => e.State == EntityState.Added))
            _hasOutboxWrite.AddOrUpdate(context, Marker);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        if (eventData.Context is { } context && _hasOutboxWrite.TryGetValue(context, out _))
        {
            _hasOutboxWrite.Remove(context);
            await context.Database.ExecuteSqlRawAsync("SELECT pg_notify({0}, '')", [OutboxChannel.For(context)], ct);
        }
        return await base.SavedChangesAsync(eventData, result, ct);
    }
}

public static class OutboxNotificationExtensions
{
    private static readonly OutboxNotifyInterceptor Interceptor = new();

    /// <summary>Emit a NOTIFY on outbox inserts so the dispatcher wakes immediately (the poll stays
    /// as the fallback). Call inside the DbContext options configuration.</summary>
    public static DbContextOptionsBuilder UseOutboxNotifications(this DbContextOptionsBuilder options) =>
        options.AddInterceptors(Interceptor);

    /// <inheritdoc cref="UseOutboxNotifications(DbContextOptionsBuilder)"/>
    public static DbContextOptionsBuilder<TContext> UseOutboxNotifications<TContext>(this DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext
    {
        options.AddInterceptors(Interceptor);
        return options;
    }
}
