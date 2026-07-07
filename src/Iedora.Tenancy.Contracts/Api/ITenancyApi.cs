namespace Iedora.Tenancy.Contracts;

/// <summary>
/// The <b>Tenancy</b> module's public API for other modules. Everything under this module's
/// <c>Contracts</c> namespace is external-use by definition: a module may import another module's
/// <c>.Contracts</c>, and NOTHING else of it. Implemented internally, resolved via DI.
/// </summary>
public interface ITenancyApi
{
    /// <summary>The user's default tenant (their earliest membership), or null if they have none.</summary>
    Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct);
}
