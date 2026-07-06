namespace Iedora.Data;

/// <summary>An organization. Users join tenants through <see cref="Membership"/>; the membership
/// with role <see cref="MembershipRoles.Owner"/> is the tenant's owner. A session pins one active
/// tenant (<see cref="Session.TenantId"/>), which the access token carries as <c>tid</c>.</summary>
public sealed class Tenant
{
    public Guid Id { get; set; }

    /// <summary>Optional URL-friendly handle. Unique when set (Postgres allows many NULLs).</summary>
    public string? Slug { get; set; }

    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
