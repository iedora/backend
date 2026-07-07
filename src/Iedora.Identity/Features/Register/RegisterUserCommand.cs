namespace Iedora.Identity;

/// <summary>Create an account. The password is hashed at the accept boundary and only the HASH
/// travels here — no plaintext password passes through the command/outbox tables.</summary>
public sealed record RegisterUserCommand(string Email, string PasswordHash, string? DisplayName)
{
    public const string Type = "identity.register-user";
}
