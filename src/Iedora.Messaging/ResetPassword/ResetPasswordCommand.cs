namespace Iedora.Messaging;

/// <summary>Apply a password reset whose token the endpoint already verified. Carries the
/// pre-computed <paramref name="PasswordHash"/> — never the plaintext. A reset severs every session,
/// so there is no family to keep.</summary>
public sealed record ResetPasswordCommand(Guid UserId, string PasswordHash)
{
    public const string Type = "identity.reset-password";
}
