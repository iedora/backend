using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Inbox;

public static class InboxExtensions
{
    /// <summary>Map the inbox ledger table (PK = the dedup key). Call from the consuming
    /// DbContext's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder MapInbox(this ModelBuilder builder)
    {
        builder.Entity<InboxMessage>(inbox =>
        {
            inbox.ToTable("inbox");
            inbox.HasKey(i => i.MessageId);
        });
        return builder;
    }

    /// <summary>Register the idempotent-consumer for the consuming service's DbContext. Supply
    /// <see cref="IInboxHandler"/>s separately.</summary>
    public static IServiceCollection AddInbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        // Per-DbContext, so one host can consume into several modules' inboxes (InboxProcessor<A> +
        // InboxProcessor<B>), each deduping in its own transaction.
        services.AddScoped<InboxProcessor<TContext>>();
        return services;
    }
}
