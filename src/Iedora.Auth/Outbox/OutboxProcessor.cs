using System.Text.Json;
using Iedora.Auth.Data;
using Iedora.Auth.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Iedora.Auth.Outbox;

/// <summary>
/// Dispatches pending outbox messages: sends the effect (email), stamps <c>ProcessedAt</c> on
/// success, or bumps <c>Attempts</c> + backs off <c>NextAttemptAt</c> on failure until a cap.
/// Called on a loop by <see cref="OutboxBackgroundService"/> (and directly by tests).
/// </summary>
public sealed class OutboxProcessor(
    AuthDbContext db, IEmailSender email, TimeProvider clock,
    IOptions<OutboxOptions> options, ILogger<OutboxProcessor> logger)
{
    private readonly OutboxOptions _opt = options.Value;

    /// <summary>Dispatch one batch of eligible messages. Returns how many were sent.</summary>
    public async Task<int> DispatchPendingAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var batch = await db.Outbox
            .Where(m => m.ProcessedAt == null
                        && m.Attempts < _opt.MaxAttempts
                        && (m.NextAttemptAt == null || m.NextAttemptAt <= now))
            .OrderBy(m => m.CreatedAt)
            .Take(_opt.BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in batch)
        {
            try
            {
                await DispatchAsync(message, ct);
                message.ProcessedAt = clock.GetUtcNow();
                sent++;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = ex.Message;
                // Linear-ish backoff, capped, so a flaky SMTP doesn't hot-loop.
                message.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(300, 5 * message.Attempts));
                logger.LogWarning(ex, "Outbox {Id} ({Type}) failed (attempt {Attempts}).", message.Id, message.Type, message.Attempts);
            }
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }

    private Task DispatchAsync(OutboxMessage message, CancellationToken ct) => message.Type switch
    {
        OutboxTypes.PasswordResetEmail => SendPasswordResetAsync(message.Payload, ct),
        _ => throw new InvalidOperationException($"Unknown outbox message type '{message.Type}'."),
    };

    private Task SendPasswordResetAsync(string payload, CancellationToken ct)
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
