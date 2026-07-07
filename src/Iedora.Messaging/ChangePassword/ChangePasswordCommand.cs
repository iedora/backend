namespace Iedora.Messaging;

/// <summary>Apply a password change the endpoint already authorized (current-password verified, or a
/// forced change) and validated (policy). Carries the pre-computed <paramref name="PasswordHash"/> —
/// never the plaintext. <paramref name="KeepFamilyId"/> is the caller's own session family, spared
/// from revocation so the current device stays signed in.</summary>
public sealed record ChangePasswordCommand(
    Guid UserId, string PasswordHash, bool ClearMustChangePassword, Guid? KeepFamilyId)
{
    public const string Type = "identity.change-password";
}
