namespace Iedora.Data;

/// <summary>A user's membership in a tenant, carrying a per-tenant <see cref="Role"/>. The
/// (<see cref="UserId"/>, <see cref="TenantId"/>) pair is the primary key — one row per user per
/// tenant. Login pins the user's earliest membership as the session's default tenant.</summary>
public sealed class Membership
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string Role { get; set; } = MembershipRoles.Member;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The per-tenant roles (stored as text, mirroring the Bun schema).</summary>
public static class MembershipRoles
{
    public const string Owner = "owner";
    public const string Member = "member";
    public const string Viewer = "viewer";
}
