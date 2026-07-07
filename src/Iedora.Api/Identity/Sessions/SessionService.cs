using ErrorOr;
using Framework.Web;
using Iedora.Api.Identity;
using Iedora.Data;
using Iedora.Api.Observability;
using Iedora.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Iedora.Api.Sessions;

/// <summary>Result of a successful rotation: the new session, its raw token, and the user.</summary>
public sealed record RotatedSession(Session Session, string Token, AppUser User);

/// <summary>
/// The refresh-token session lifecycle. A login opens a family (<see cref="CreateAsync"/>);
/// each refresh rotates it (<see cref="RotateAsync"/>) via a guarded atomic UPDATE that also
/// detects reuse; logout revokes a family or all of a user's sessions. The raw token lives
/// only in the client cookie — the DB holds SHA-256 digests.
/// </summary>
public sealed class SessionService(IdentityDbContext db, TimeProvider clock, IOptions<SessionSettings> options)
{
    private readonly SessionSettings _opt = options.Value;

    /// <summary>Opens a new session family at login.</summary>
    public async Task<(Session session, string token)> CreateAsync(
        Guid userId, Guid? tenantId, RequestMeta meta, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var (token, hash) = RefreshTokens.New();
        var session = new Session
        {
            Id = Guid.CreateVersion7(),
            FamilyId = Guid.NewGuid(),
            UserId = userId,
            TenantId = tenantId,
            TokenHash = hash,
            IssuedAt = now,
            ExpiresAt = now + _opt.RefreshTtl,
            AbsoluteExpiresAt = now + _opt.RefreshAbsoluteTtl,
            UserAgent = meta.UserAgent,
            Ip = meta.Ip,
        };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);
        return (session, token);
    }

    /// <summary>
    /// Rotates the refresh token. Returns a distinct <see cref="IdentityErrors"/> for each invalid
    /// case (all map to 401, but with different codes for the client + logs). Presenting a spent
    /// token, or losing the concurrent-rotation race, burns the whole family (token-theft response).
    /// </summary>
    public async Task<ErrorOr<RotatedSession>> RotateAsync(string rawToken, RequestMeta meta, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = RefreshTokens.Hash(rawToken);

        var current = await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (current is null) return IdentityErrors.InvalidRefreshToken;   // unknown token

        if (current.IsRotated)                                        // spent token replayed → theft
        {
            await BurnFamilyAsync(current.FamilyId, ct);
            return IdentityErrors.RefreshTokenReuse;
        }
        if (!current.IsLive(now)) return IdentityErrors.InvalidRefreshToken; // expired / revoked

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is null)                                             // user removed
        {
            await BurnFamilyAsync(current.FamilyId, ct);
            return IdentityErrors.InvalidRefreshToken;
        }

        var (token, newHash) = RefreshTokens.New();
        var successor = new Session
        {
            Id = Guid.CreateVersion7(),
            FamilyId = current.FamilyId,                        // same chain
            UserId = current.UserId,
            TenantId = current.TenantId,
            TokenHash = newHash,
            IssuedAt = now,
            ExpiresAt = now + _opt.RefreshTtl,                  // sliding renewal
            AbsoluteExpiresAt = current.AbsoluteExpiresAt,      // hard cap unchanged
            UserAgent = meta.UserAgent,
            Ip = meta.Ip,
        };

        // Insert successor + guarded UPDATE atomically. The Aspire Npgsql integration enables
        // a retrying execution strategy, so the transaction must run as one retriable unit.
        var strategy = db.Database.CreateExecutionStrategy();
        var won = await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            db.Sessions.Add(successor);
            await db.SaveChangesAsync(ct);

            // Only wins if the old row is still unrotated + unrevoked. If a concurrent refresh
            // already rotated it, 0 rows change: we lost the race → reuse.
            var updated = await db.Sessions
                .Where(s => s.Id == current.Id && s.ReplacedBy == null && s.RevokedAt == null)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(s => s.ReplacedBy, successor.Id)
                    .SetProperty(s => s.RevokedAt, now), ct);

            if (updated == 0)
            {
                await tx.RollbackAsync(ct);
                return false;
            }
            await tx.CommitAsync(ct);
            return true;
        });

        if (!won)
        {
            await BurnFamilyAsync(current.FamilyId, ct);
            return IdentityErrors.RefreshTokenReuse;
        }

        Telemetry.SessionsRotated.Add(1);
        return new RotatedSession(successor, token, user);
    }

    /// <summary>Revoke every live session in a family (logout this device / reuse burn).</summary>
    public Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.Sessions
            .Where(s => s.FamilyId == familyId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTimeOffset?)now), ct);
    }

    /// <summary>The user's sessions, newest first — the device-history view. Includes revoked +
    /// expired rows (the caller derives "current" from <see cref="Session.IsLive"/>). Read-only;
    /// the secret token hash never leaves the server.</summary>
    public Task<List<Session>> ListForUserAsync(Guid userId, int limit, CancellationToken ct) =>
        db.Sessions.AsNoTracking()
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.IssuedAt)
            .Take(limit)
            .ToListAsync(ct);

    /// <summary>Revoke one device (family), scoped to its owner so a user can only sign out their
    /// OWN devices. Returns true if a live session was revoked (false ⇒ unknown/already dead).</summary>
    public async Task<bool> RevokeFamilyForUserAsync(Guid userId, Guid familyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var revoked = await db.Sessions
            .Where(s => s.UserId == userId && s.FamilyId == familyId && s.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTimeOffset?)now), ct);
        return revoked > 0;
    }

    /// <summary>Revoke every live session for a user, optionally keeping one family alive
    /// (e.g. the current device after a password change). Pass null to revoke all.</summary>
    public Task<int> RevokeAllForUserAsync(Guid userId, Guid? exceptFamilyId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.Sessions.Where(s => s.UserId == userId && s.RevokedAt == null);
        if (exceptFamilyId is { } family) query = query.Where(s => s.FamilyId != family);
        return query.ExecuteUpdateAsync(u => u.SetProperty(s => s.RevokedAt, (DateTimeOffset?)now), ct);
    }

    /// <summary>Revoke the family a raw refresh token belongs to. Idempotent: an unknown
    /// token is a no-op (logout returns 200 regardless).</summary>
    public async Task RevokeFamilyByTokenAsync(string rawToken, CancellationToken ct)
    {
        var hash = RefreshTokens.Hash(rawToken);
        var session = await db.Sessions.FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null) await RevokeFamilyAsync(session.FamilyId, ct);
    }

    private async Task BurnFamilyAsync(Guid familyId, CancellationToken ct)
    {
        await RevokeFamilyAsync(familyId, ct);
        Telemetry.ReuseDetected.Add(1);
    }
}
