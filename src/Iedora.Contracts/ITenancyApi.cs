namespace Iedora.Contracts;

/// <summary>
/// The <b>Tenancy</b> module's public cross-module API. Other modules depend on THIS contract, not
/// on the Tenancy module — implemented inside Tenancy, resolved via DI. Identity's login uses it to
/// resolve a user's default tenant without touching tenancy tables.
/// </summary>
public interface ITenancyApi
{
    /// <summary>The user's default tenant (their earliest membership), or null if they have none.</summary>
    Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct);
}
