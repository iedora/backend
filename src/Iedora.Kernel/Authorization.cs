namespace Iedora.Kernel;

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

    /// <summary>Requires an internal service token (<c>typ=service</c>). Applied to
    /// service-to-service endpoints (e.g. the menu BFF's user-admin CRM reads).</summary>
    public const string Service = "Service";
}

/// <summary>JWT <c>typ</c> claim values. <c>access</c> is a user access token; <c>service</c> is an
/// internal service token minted by the client-credentials grant.</summary>
public static class TokenTypes
{
    public const string Access = "access";
    public const string Service = "service";
}
