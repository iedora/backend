using Iedora.Data;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Api.Features.Tenants;

/// <summary>
/// The Tenancy module's cross-module surface. Other modules (e.g. Identity's login) depend on
/// THIS, never on <see cref="TenancyDbContext"/> directly — an in-process method call that keeps
/// tenancy tables private to this module.
/// </summary>
public interface ITenancyApi
{
    /// <summary>The user's default tenant (their earliest membership), or null if they have none.</summary>
    Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct);
}

internal sealed class TenancyApi(TenancyDbContext db) : ITenancyApi
{
    public Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct) =>
        db.Memberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.TenantId)
            .FirstOrDefaultAsync(ct);
}
