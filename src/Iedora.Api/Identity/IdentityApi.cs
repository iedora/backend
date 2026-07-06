using Iedora.Api.Identity.Contracts;
using Iedora.Data;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Api.Identity;

/// <summary>Identity's implementation of the <see cref="IIdentityApi"/> contract. Internal — other
/// modules only ever see the contract (this module's <c>Contracts</c> namespace), never this class
/// or the identity tables it reads.</summary>
internal sealed class IdentityApi(IdentityDbContext db) : IIdentityApi
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName))
            .ToListAsync(ct);
    }
}
