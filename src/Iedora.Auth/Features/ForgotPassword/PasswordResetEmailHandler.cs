using System.Text.Json;
using Iedora.Auth.Email;
using Iedora.Framework.Outbox;

namespace Iedora.Auth.Features.ForgotPassword;

public sealed record PasswordResetEmail(string Email, string ResetLink);

/// <summary>Sends the reset email for a queued <see cref="PasswordResetEmail"/>. Registered as an
/// <see cref="IOutboxHandler"/>; the generic dispatcher routes messages of this type here.</summary>
public sealed class PasswordResetEmailHandler(IEmailSender email) : IOutboxHandler
{
    public const string MessageType = "password-reset-email";
    public string Type => MessageType;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var msg = JsonSerializer.Deserialize<PasswordResetEmail>(payload)!;
        var html =
            $"""
            <p>We received a request to reset your iedora password.</p>
            <p><a href="{msg.ResetLink}">Reset your password</a></p>
            <p>If you didn't request this, you can safely ignore this email.</p>
            """;
        return email.SendAsync(msg.Email, "Reset your password", html, ct);
    }
}
