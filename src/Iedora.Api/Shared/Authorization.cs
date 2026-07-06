namespace Iedora.Api.Shared;

/// <summary>Cross-cutting authorization constants (shared kernel — every module may reference).</summary>
public static class Roles
{
    /// <summary>Platform staff/admin. Rides in the access token's <c>roles</c> claim.</summary>
    public const string Admin = "admin";
}

public static class Policies
{
    /// <summary>Requires the <see cref="Roles.Admin"/> role. Applied to admin-only endpoints.</summary>
    public const string Admin = "Admin";
}
