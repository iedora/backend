namespace Iedora.Identity.Contracts;

/// <summary>A minimal projection of a user, for other modules to hydrate references held by id.</summary>
public sealed record UserSummary(Guid Id, string Email, string? Name);

/// <summary>A user as the staff Users CRM lists/shows them — never the password hash. Roles come
/// from the ASP.NET Identity role tables (the same set that drives the JWT <c>roles</c> claim).</summary>
public sealed record AdminUserRecord(
    Guid Id, string Email, string? Name, IReadOnlyList<string> Roles,
    DateTimeOffset CreatedAt, DateTimeOffset? PasswordChangedAt, bool EmailConfirmed, bool MustChangePassword);

/// <summary>One of a user's refresh-token sessions (device history). <c>Current</c> = still live.</summary>
public sealed record UserSessionRecord(
    Guid Id, Guid FamilyId, Guid? TenantId, string? Ip, string? UserAgent,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, bool Current);

/// <summary>The outcome of an admin set-password.</summary>
public enum UserPasswordOutcome { Updated, UserNotFound, Rejected }

/// <summary>
/// The <b>Identity</b> module's public API for other modules. Everything under this module's
/// <c>Contracts</c> namespace is external-use by definition: a module may import another module's
/// <c>.Contracts</c>, and NOTHING else of it. Implemented internally, resolved via DI.
/// </summary>
public interface IIdentityApi
{
    /// <summary>Summaries for the given user ids (missing ids are simply absent from the result).</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    // --- Staff Users CRM (the menu module is the BFF; the audit timeline is read separately). ---

    /// <summary>Search/list users for the admin CRM (email/name, newest first). <paramref name="limit"/>
    /// is clamped to 1..200.</summary>
    Task<IReadOnlyList<AdminUserRecord>> ListUsersAsync(string? query, int limit, CancellationToken ct);

    /// <summary>One user's admin record, or null if there's no such user.</summary>
    Task<AdminUserRecord?> GetUserAsync(Guid id, CancellationToken ct);

    /// <summary>A user's sessions (device history, newest first). Empty for an unknown user.</summary>
    Task<IReadOnlyList<UserSessionRecord>> ListUserSessionsAsync(Guid id, CancellationToken ct);

    /// <summary>Force a password change at next login + sign the user out everywhere. False if the
    /// user doesn't exist.</summary>
    Task<bool> ForcePasswordChangeAsync(Guid id, CancellationToken ct);

    /// <summary>Set a temporary password (forces a change at next login + signs the user out). The
    /// plaintext is hashed here and never persisted anywhere but the user's hash.</summary>
    Task<UserPasswordOutcome> SetUserPasswordAsync(Guid id, string password, CancellationToken ct);

    /// <summary>Revoke one of a user's sessions (a device/family). False if that family isn't a live
    /// session of the user.</summary>
    Task<bool> RevokeUserSessionAsync(Guid id, Guid family, CancellationToken ct);
}
