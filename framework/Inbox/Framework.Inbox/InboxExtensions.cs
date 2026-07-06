using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<InboxProcessor>();
        return services;
    }
}
