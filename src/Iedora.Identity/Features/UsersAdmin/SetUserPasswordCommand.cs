namespace Iedora.Identity;

/// <summary>Admin action: set a temporary password for a target user. Carries the pre-computed
/// <paramref name="PasswordHash"/> — never the plaintext. The user must change it at next login and
/// every session is revoked, so the temporary password is what they sign in with.</summary>
public sealed record SetUserPasswordCommand(Guid UserId, string PasswordHash)
{
    public const string Type = "identity.admin.set-password";
}
