using System.ComponentModel.DataAnnotations;
using Framework.Commands;
using Framework.Web;
using Iedora.Kernel;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

public sealed record ResetPasswordRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Token,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password);

// POST /auth/reset-password — set a new password from the emailed reset token (async). The token is
// verified synchronously (no side effect) and the new password is policy-checked; an unknown email or
// a bad/expired token both yield the same 400 (no enumeration). Then the hash is computed and a command
// submitted → 202. The handler swaps the hash and revokes every session; poll the status URL.
public static class ResetPasswordEndpoint
{
    // Identity's stable purpose string for password-reset tokens (GeneratePasswordResetTokenAsync).
    private const string ResetPurpose = "ResetPassword";

    public static void MapResetPassword(this RouteGroupBuilder group) =>
        group.MapPost("/reset-password", async (
                ResetPasswordRequest req, UserManager<AppUser> users, IdentityDbContext db,
                TimeProvider clock, CancellationToken ct) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) return ProblemResults.From(IdentityErrors.InvalidResetToken); // == bad token

            var tokenValid = await users.VerifyUserTokenAsync(
                user, users.Options.Tokens.PasswordResetTokenProvider, ResetPurpose, req.Token);
            if (!tokenValid) return ProblemResults.From(IdentityErrors.InvalidResetToken);

            var policy = await PasswordPolicy.ValidateAsync(users, user, req.Password);
            if (policy.IsError) return ProblemResults.From(policy.Errors);

            var passwordHash = users.PasswordHasher.HashPassword(user, req.Password);
            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, ResetPasswordCommand.Type,
                new ResetPasswordCommand(user.Id, passwordHash), clock);
            await db.SaveChangesAsync(ct);

            return CommandEndpoints.AcceptedCommand(commandId, "/auth");
        })
        .AllowAnonymous()
        .WithName("ResetPassword")
        .WithSummary("Set a new password using the emailed reset token (async — poll the status URL).")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest);
}
