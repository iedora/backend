using Iedora.Data;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Api.Identity;

/// <summary>A minimal projection of a user, for other modules to hydrate references held by id.</summary>
public sealed record UserSummary(Guid Id, string Email, string? Name);

/// <summary>
/// The Identity module's cross-module surface (mirror of Tenancy's <c>ITenancyApi</c>): other
/// modules resolve users by id through THIS, never by querying the identity tables. Depending on
/// this public interface is fine; reaching into the module's internals (DbContext, errors) is not.
/// </summary>
public interface IIdentityApi
{
    /// <summary>Summaries for the given user ids (missing ids are simply absent from the result).</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}

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
