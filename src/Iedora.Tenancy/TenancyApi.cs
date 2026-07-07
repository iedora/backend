using Iedora.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Tenancy;

/// <summary>Tenancy's implementation of the <see cref="ITenancyApi"/> contract. Internal — other
/// modules only ever see the contract (this module's <c>Contracts</c> namespace), never this class
/// or the tenancy tables it reads.</summary>
internal sealed class TenancyApi(TenancyDbContext db) : ITenancyApi
{
    public Task<Guid?> GetDefaultTenantAsync(Guid userId, CancellationToken ct) =>
        db.Memberships
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => (Guid?)m.TenantId)
            .FirstOrDefaultAsync(ct);
}
