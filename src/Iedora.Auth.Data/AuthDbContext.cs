using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Auth.Data;

/// <summary>
/// The Identity store — full ASP.NET Core Identity schema (AspNetUsers, AspNetRoles,
/// AspNetUserRoles, ...) on Postgres with Guid keys, plus the refresh-token
/// <see cref="Sessions"/> table. Schema is applied via EF migrations (see SchemaInitializer).
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

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
        });
    }
}
