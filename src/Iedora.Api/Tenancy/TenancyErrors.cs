using ErrorOr;

namespace Iedora.Api.Tenancy;

/// <summary>
/// The Tenancy module's own error catalog — private to this module (the Identity module has its own
/// <c>IdentityErrors</c>; neither references the other's). Cross-cutting errors live in the shared
/// kernel's <c>CommonErrors</c>.
/// </summary>
public static class TenancyErrors
{
    /// <summary>Admin-create referenced an owner user id that doesn't exist. Maps to 400.</summary>
    public static readonly Error UnknownOwner =
        Error.Validation("tenancy.unknown_owner", "The owner user does not exist.");

    /// <summary>Operation targeted a tenant that doesn't exist. Maps to 404.</summary>
    public static readonly Error TenantNotFound =
        Error.NotFound("tenancy.tenant_not_found", "The tenant does not exist.");
}
