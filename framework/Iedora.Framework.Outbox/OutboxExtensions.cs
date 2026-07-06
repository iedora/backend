using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Framework.Outbox;

public static class OutboxExtensions
{
    /// <summary>Map the outbox table + a filtered index on pending rows. Call from the consuming
    /// DbContext's <c>OnModelCreating</c>.</summary>
    public static ModelBuilder MapOutbox(this ModelBuilder builder)
    {
        builder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox");
            outbox.HasKey(o => o.Id);
            // Poll query is WHERE ProcessedAt IS NULL ORDER BY CreatedAt — a composite index
            // serves it without a provider-specific partial-index filter.
            outbox.HasIndex(o => new { o.ProcessedAt, o.CreatedAt });
        });
        return builder;
    }

    /// <summary>Stage an outbox message on the DbContext — committed by the caller's
    /// <c>SaveChangesAsync</c>, in the SAME transaction as any domain change.</summary>
    public static void EnqueueOutbox(this DbContext db, string type, object payload, TimeProvider clock) =>
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = clock.GetUtcNow(),
        });

    /// <summary>Register the outbox dispatcher for the consuming service's DbContext. Bind
    /// <see cref="OutboxOptions"/> (section "Outbox") and register <see cref="IOutboxHandler"/>s
    /// separately.</summary>
    public static IServiceCollection AddOutbox<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        // Expose the concrete DbContext as its base so the processor stays context-agnostic.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<OutboxProcessor>();
        services.AddHostedService<OutboxBackgroundService>();
        return services;
    }
}
