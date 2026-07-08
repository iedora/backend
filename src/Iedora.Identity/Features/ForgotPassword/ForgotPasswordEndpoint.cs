using System.ComponentModel.DataAnnotations;
using Framework.Outbox;
using Framework.Notifications.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Iedora.Identity;

public sealed record ForgotPasswordRequest([property: Required, EmailAddress] string Email);

// POST /auth/forgot-password — ALWAYS 200 (no account enumeration). For a real account, render the
// reset email (Identity owns the wording) and stage an EmailRequested on the outbox, committed in one
// SaveChangesAsync. The Notifications service relays it into its inbox and delivers it over SMTP —
// so the send is off the request path and survives a crash after commit.
public static class ForgotPasswordEndpoint
{
    public static void MapForgotPassword(this RouteGroupBuilder group) =>
        group.MapPost("/forgot-password", async (
                ForgotPasswordRequest req, UserManager<AppUser> users, IdentityDbContext db,
                TimeProvider clock, IOptions<PasswordResetOptions> options, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is not null)
            {
                var token = await users.GeneratePasswordResetTokenAsync(user);
                var link = $"{options.Value.ResetUrlBase}?email={Uri.EscapeDataString(req.Email)}&token={Uri.EscapeDataString(token)}";
                var html =
                    $"""
                    <p>We received a request to reset your iedora password.</p>
                    <p><a href="{link}">Reset your password</a></p>
                    <p>If you didn't request this, you can safely ignore this email.</p>
                    """;
                db.EnqueueOutbox(EmailRequested.Type,
                    new EmailRequested(Guid.CreateVersion7(), req.Email, "Reset your password", html), clock);
                await db.SaveChangesAsync(ct);
            }
            return TypedResults.Ok(); // identical response whether or not the account exists
        })
        .AllowAnonymous()
        .WithName("ForgotPassword")
        .WithSummary("Request a password-reset email (always 200 — no account enumeration).")
        .Produces(StatusCodes.Status200OK);
}
