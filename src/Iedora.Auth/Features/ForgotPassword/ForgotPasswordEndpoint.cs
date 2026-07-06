using System.ComponentModel.DataAnnotations;
using Iedora.Auth.Common;
using Iedora.Auth.Data;
using Iedora.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Iedora.Auth.Features.ForgotPassword;

public sealed record ForgotPasswordRequest([property: Required, EmailAddress] string Email);

// POST /auth/forgot-password — ALWAYS 200 (no account enumeration). For a real account, generate
// an Identity reset token and stage the reset email in the transactional outbox, committed in one
// SaveChangesAsync so the email survives a crash after commit (delivered by OutboxBackgroundService).
public static class ForgotPasswordEndpoint
{
    public static void MapForgotPassword(this RouteGroupBuilder group) =>
        group.MapPost("/forgot-password", async (
                ForgotPasswordRequest req, UserManager<AppUser> users, AuthDbContext db,
                TimeProvider clock, IOptions<PasswordResetOptions> options, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is not null)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var link = $"{options.Value.ResetUrlBase}?email={Uri.EscapeDataString(req.Email)}&token={Uri.EscapeDataString(token)}";
                db.EnqueueOutbox(PasswordResetEmailHandler.MessageType, new PasswordResetEmail(req.Email, link), clock);
                await db.SaveChangesAsync(ct);
            }
            return TypedResults.Ok(); // identical response whether or not the account exists
        })
        .AllowAnonymous()
        .WithName("ForgotPassword")
        .WithSummary("Request a password-reset email (always 200 — no account enumeration).")
        .Produces(StatusCodes.Status200OK);
}
