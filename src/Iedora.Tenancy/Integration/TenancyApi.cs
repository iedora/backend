using Iedora.Identity.Contracts;
using Iedora.Tenancy.Contracts;
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

    public async Task<IReadOnlyList<MembershipRecord>> MembershipsForUserAsync(Guid userId, CancellationToken ct)
    {
        // Fetch role as the enum, stringify in memory (EF can't translate enum.ToString()).
        var rows = await db.Memberships.AsNoTracking()
            .Where(m => m.UserId == userId).OrderBy(m => m.CreatedAt)
            .Select(m => new { m.TenantId, m.Role }).ToListAsync(ct);
        return rows.Select(r => new MembershipRecord(r.TenantId, r.Role.ToString())).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> TenantCountsAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return new Dictionary<Guid, int>();
        var counts = await db.Memberships.AsNoTracking()
            .Where(m => userIds.Contains(m.UserId))
            .GroupBy(m => m.UserId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return counts.ToDictionary(x => x.Key, x => x.Count);
    }
}
