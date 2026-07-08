using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Identity;

/// <summary>
/// Applies a password-lifecycle change to a user off the request path — the shared tail of every
/// password command (self-service change/reset and admin set/force-change). Any plaintext was hashed
/// synchronously at the endpoint, so only the hash reaches here (never the password itself). Rolling
/// the security stamp invalidates outstanding reset/forced-change tokens; the session sweep drops the
/// user's live refresh sessions (optionally sparing the current device). Idempotent under redelivery:
/// re-applying the same hash and re-revoking already-dead sessions are both no-ops.
/// </summary>
internal static class PasswordCredential
{
    /// <param name="passwordHash">The new hash, or null to leave the password unchanged (force-change).</param>
    /// <param name="mustChangePassword">The forced-change flag's final state (cleared on a normal
    /// change/reset, set when an admin forces a change).</param>
    /// <param name="keepFamilyId">A session family to spare from revocation (the caller's own device),
    /// or null to revoke every session.</param>
    public static async Task<ErrorOr<string?>> ApplyAsync(
        IdentityDbContext db, TimeProvider clock,
        Guid userId, string? passwordHash, bool mustChangePassword, Guid? keepFamilyId,
        CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
            return Error.Unauthorized("auth.user_gone", "The user no longer exists.");

        // Tracked mutation — committed by the CommandHandler's SaveChanges alongside the status flip.
        var now = clock.GetUtcNow();
        if (passwordHash is not null)
        {
            user.PasswordHash = passwordHash;
            user.PasswordChangedAt = now; // "last password change" for the staff CRM + owner settings
        }
        user.SecurityStamp = Guid.NewGuid().ToString(); // invalidate stamp-derived tokens
        user.MustChangePassword = mustChangePassword;

        // Revoke live sessions (immediate bulk update). A voluntary change keeps the current device;
        // a reset / admin action severs everything.
        var sessions = db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (keepFamilyId is { } family) sessions = sessions.Where(s => s.FamilyId != family);
        await sessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTimeOffset?)now), ct);

        return (string?)null; // a credential change has no addressable result location
    }
}
