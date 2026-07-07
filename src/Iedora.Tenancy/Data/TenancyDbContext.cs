using Framework.Commands;
using Framework.Inbox;
using Framework.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Tenancy;

/// <summary>
/// The <b>Tenancy</b> module's store, under the <c>tenancy</c> Postgres schema: tenants and their
/// memberships. A membership references a user by id only (<see cref="Membership.UserId"/>) —
/// there is NO cross-module FK to AspNetUsers; the Identity module owns users, and this module
/// resolves them by id. Applied via this context's own EF migrations.
/// </summary>
public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : DbContext(options)
{
    public const string Schema = "tenancy";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Membership> Memberships => Set<Membership>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);

        builder.MapOutbox();   // async-write pipeline: this module's outbox
        builder.MapInbox();    // + idempotent consumer (cross-module events)
        builder.MapCommands(); // + command-status tracking

        builder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Name).IsRequired();
        });

        builder.Entity<Membership>(membership =>
        {
            membership.ToTable("memberships");
            membership.HasKey(m => new { m.UserId, m.TenantId }); // one row per user per tenant
            membership.Property(m => m.Role)
                .HasConversion<string>() // persist the enum name (text); the app always sets it
                .IsRequired();

            membership.HasOne<Tenant>()
                .WithMany()
                .HasForeignKey(m => m.TenantId)
                .OnDelete(DeleteBehavior.Cascade);
            // No FK on UserId — cross-module reference (Identity owns users).
        });
    }
}
