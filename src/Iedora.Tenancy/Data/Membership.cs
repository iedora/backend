namespace Iedora.Tenancy;

/// <summary>A user's membership in a tenant, carrying a per-tenant <see cref="Role"/>. The
/// (<see cref="UserId"/>, <see cref="TenantId"/>) pair is the primary key — one row per user per
/// tenant. Login pins the user's earliest membership as the session's default tenant.</summary>
public sealed class Membership
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public MembershipRole Role { get; set; } = MembershipRole.Member;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A user's role within a tenant. Persisted as its name (text) via a value conversion.
/// <see cref="Member"/> is the zero value, so an unset role defaults to the least-privileged.</summary>
public enum MembershipRole
{
    Member,
    Owner,
    Viewer,
}
