namespace Iedora.Tenancy.Contracts;

/// <summary>A user's membership in one tenant — the tenant id + the user's role there.</summary>
public sealed record MembershipRecord(Guid TenantId, string Role);

/// <summary>
/// The <b>Tenancy</b> module's public API for other modules. Everything under this module's
/// <c>Contracts</c> namespace is external-use by definition: a module may import another module's
/// <c>.Contracts</c>, and NOTHING else of it. Implemented internally, resolved via DI.
/// </summary>
public interface ITenancyApi
{
    /// <summary>The user's default tenant (their earliest membership), or null if they have none.</summary>
    Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct);

    /// <summary>A user's tenant memberships (earliest first) — for the staff Users CRM detail.</summary>
    Task<IReadOnlyList<MembershipRecord>> MembershipsForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>How many tenants each of the given users belongs to (missing ⇒ 0) — one aggregate pass
    /// for the staff Users list.</summary>
    Task<IReadOnlyDictionary<Guid, int>> TenantCountsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct);
}
