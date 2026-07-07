using ErrorOr;
using Iedora.Data;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Messaging;

/// <summary>
/// Applies a new password hash to a user off the request path — the shared tail of the
/// change-password and reset-password commands. The plaintext was hashed synchronously at the
/// endpoint, so only the hash reaches here (never the password itself). Rolling the security stamp
/// invalidates any outstanding reset/forced-change tokens; the session sweep drops the user's live
/// refresh sessions (optionally sparing the current device). Idempotent under redelivery: re-applying
/// the same hash and re-revoking already-dead sessions are both no-ops.
/// </summary>
internal static class PasswordCredential
{
    public static async Task<ErrorOr<string?>> ApplyAsync(
        IdentityDbContext db, TimeProvider clock,
        Guid userId, string passwordHash, bool clearMustChangePassword, Guid? keepFamilyId,
        CancellationToken ct)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null)
            return Error.Unauthorized("auth.user_gone", "The authenticated user no longer exists.");

        // Tracked mutation — committed by the CommandHandler's SaveChanges alongside the status flip.
        user.PasswordHash = passwordHash;
        user.SecurityStamp = Guid.NewGuid().ToString(); // invalidate stamp-derived tokens
        if (clearMustChangePassword) user.MustChangePassword = false;

        // Revoke live sessions (immediate bulk update). A password change keeps the current device;
        // a reset severs everything.
        var now = clock.GetUtcNow();
        var sessions = db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (keepFamilyId is { } family) sessions = sessions.Where(s => s.FamilyId != family);
        await sessions.ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTimeOffset?)now), ct);

        return (string?)null; // a credential change has no addressable result location
    }
}
