using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Identity;

/// <summary>Identity's implementation of the <see cref="IIdentityApi"/> contract. Internal — other
/// modules only ever see the contract (this module's <c>Contracts</c> namespace), never this class
/// or the identity tables it reads.</summary>
internal sealed class IdentityApi(
    IdentityDbContext db, UserManager<AppUser> users, SessionService sessions, TimeProvider clock) : IIdentityApi
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AdminUserRecord>> ListUsersAsync(string? query, int limit, CancellationToken ct)
    {
        var take = Math.Clamp(limit, 1, 200);
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = $"%{query.Trim()}%";
            q = q.Where(u => EF.Functions.ILike(u.Email!, like)
                || (u.DisplayName != null && EF.Functions.ILike(u.DisplayName, like)));
        }

        var rows = await q.OrderByDescending(u => u.CreatedAt).Take(take).ToListAsync(ct);
        var roles = await RolesForAsync(rows.Select(u => u.Id).ToList(), ct);
        return rows.Select(u => Map(u, roles.GetValueOrDefault(u.Id, []))).ToList();
    }

    public async Task<AdminUserRecord?> GetUserAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return null;
        var roles = await RolesForAsync([id], ct);
        return Map(user, roles.GetValueOrDefault(id, []));
    }

    public async Task<IReadOnlyList<UserSessionRecord>> ListUserSessionsAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var rows = await sessions.ListForUserAsync(id, 50, ct);
        return rows.Select(s => new UserSessionRecord(
            s.Id, s.FamilyId, s.TenantId, s.Ip, s.UserAgent,
            s.IssuedAt, s.ExpiresAt, s.RevokedAt, s.IsLive(now))).ToList();
    }

    public async Task<bool> ForcePasswordChangeAsync(Guid id, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null) return false;

        user.MustChangePassword = true;
        user.SecurityStamp = Guid.NewGuid().ToString(); // invalidate stamp-derived tokens
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllForUserAsync(id, exceptFamilyId: null, ct); // sign out every device
        return true;
    }

    public async Task<UserPasswordOutcome> SetUserPasswordAsync(Guid id, string password, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return UserPasswordOutcome.UserNotFound;

        // Hash + validate via Identity (rolls the security stamp). The reset-token path avoids the old one.
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, password);
        if (!result.Succeeded) return UserPasswordOutcome.Rejected;

        user.MustChangePassword = true; // a temporary password — force a change at next login
        user.PasswordChangedAt = clock.GetUtcNow();
        await users.UpdateAsync(user);
        await sessions.RevokeAllForUserAsync(id, exceptFamilyId: null, ct); // the temp password is what they sign in with
        return UserPasswordOutcome.Updated;
    }

    public Task<bool> RevokeUserSessionAsync(Guid id, Guid family, CancellationToken ct) =>
        sessions.RevokeFamilyForUserAsync(id, family, ct);

    // Roles per user id from the ASP.NET Identity role tables, in one query (no N+1).
    private async Task<Dictionary<Guid, IReadOnlyList<string>>> RolesForAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var pairs = await (
            from ur in db.UserRoles
            join r in db.Roles on ur.RoleId equals r.Id
            where ids.Contains(ur.UserId)
            select new { ur.UserId, r.Name }).ToListAsync(ct);
        return pairs.GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(x => x.Name!).OrderBy(n => n).ToList());
    }

    private static AdminUserRecord Map(AppUser u, IReadOnlyList<string> roles) => new(
        u.Id, u.Email!, u.DisplayName, roles, u.CreatedAt, u.PasswordChangedAt, u.EmailConfirmed, u.MustChangePassword);
}
