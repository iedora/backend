using System.ComponentModel.DataAnnotations;
using Framework.Web;
using ErrorOr;
using Iedora.Api.Common;
using Iedora.Data;
using Iedora.Api.Sessions;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Api.Features.ResetPassword;

public sealed record ResetPasswordRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Token,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password);

// POST /auth/reset-password — set a new password from the emailed reset token. No enumeration:
// an unknown email or a bad/expired token both yield the same 400. On success, revoke all the
// user's sessions (a password reset is a credential change).
public static class ResetPasswordEndpoint
{
    public static void MapResetPassword(this RouteGroupBuilder group) =>
        group.MapPost("/reset-password", async (
                ResetPasswordRequest req, UserManager<AppUser> users, SessionService sessions, CancellationToken ct) =>
        {
            var result = await ResetAsync(users, sessions, req, ct);
            return result.IsError ? ProblemResults.From(result.Errors) : TypedResults.NoContent();
        })
        .AllowAnonymous()
        .WithName("ResetPassword")
        .WithSummary("Set a new password using the emailed reset token.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<ErrorOr<Success>> ResetAsync(
        UserManager<AppUser> users, SessionService sessions, ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return AuthErrors.InvalidResetToken; // same as a bad token (no enumeration)

        var result = await users.ResetPasswordAsync(user, req.Token, req.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken")) return AuthErrors.InvalidResetToken;
            return result.Errors.Select(e => Error.Validation($"auth.{e.Code}", e.Description)).ToList();
        }

        // A credential change invalidates every existing session.
        await sessions.RevokeAllForUserAsync(user.Id, exceptFamilyId: null, ct);
        return Result.Success;
    }
}
