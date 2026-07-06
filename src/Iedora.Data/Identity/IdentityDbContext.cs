using Framework.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Data;

/// <summary>
/// The <b>Identity</b> module's store, under the <c>identity</c> Postgres schema: the full
/// ASP.NET Core Identity schema (AspNetUsers, AspNetRoles, …) with Guid keys, the refresh-token
/// <see cref="Sessions"/> table, and the transactional outbox (from Framework.Outbox). Schema is
/// applied via this context's own EF migrations (see the migration worker). The base type is
/// fully qualified to avoid clashing with this class's name.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public const string Schema = "identity";

    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.MapOutbox(); // reusable outbox table (owned by this module)

        builder.Entity<Session>(session =>
        {
            session.ToTable("sessions");
            session.HasKey(s => s.Id);

            // One row per refresh token; looked up by its SHA-256 digest.
            session.HasIndex(s => s.TokenHash).IsUnique();
            session.Property(s => s.TokenHash).IsRequired();

            // Family rotation chain + fast "live sessions for a user" scans.
            session.HasIndex(s => s.FamilyId);
            session.HasIndex(s => s.UserId).HasFilter("\"RevokedAt\" IS NULL");

            session.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(s => s.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // Session.TenantId is a soft reference into the Tenancy module — no cross-module FK.
        });

        builder.Entity<OutboxMessage>(outbox =>
        {
            outbox.ToTable("outbox");
            outbox.HasKey(o => o.Id);
            // The dispatcher polls pending rows in creation order — index just the unprocessed ones.
            outbox.HasIndex(o => o.CreatedAt).HasFilter("\"ProcessedAt\" IS NULL");
        });
    }
}
