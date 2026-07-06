using System.Text.Json;
using Iedora.Auth.Data;

namespace Iedora.Auth.Outbox;

public sealed class OutboxOptions
{
    public int PollSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 10;
}

/// <summary>Logical outbox message types the dispatcher routes on.</summary>
public static class OutboxTypes
{
    public const string PasswordResetEmail = "password-reset-email";
}

public sealed record PasswordResetEmail(string Email, string ResetLink);

public static class OutboxWriter
{
    /// <summary>Stage an outbox message on the DbContext — committed by the caller's
    /// SaveChangesAsync, in the SAME transaction as any domain change.</summary>
    public static void Enqueue(this AuthDbContext db, string type, object payload, TimeProvider clock) =>
        db.Outbox.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = type,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = clock.GetUtcNow(),
        });
}
