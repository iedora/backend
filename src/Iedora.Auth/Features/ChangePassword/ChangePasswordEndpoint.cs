using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ErrorOr;
using Iedora.Auth.Common;
using Iedora.Auth.Data;
using Iedora.Auth.Sessions;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.ChangePassword;

public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 8)] string NewPassword);

// POST /auth/change-password — change the signed-in user's password.
// Voluntary change requires the current password; a forced change (MustChangePassword, set by an
// admin) skips it since the user just authenticated. On success the flag clears and every OTHER
// session is revoked (the current device stays signed in). The handler returns ErrorOr<Success>;
// each failure is a typed error mapped to its status (missing current → 400, wrong current → 403,
// weak password → 400 validation).
public static class ChangePasswordEndpoint
{
    public static void MapChangePassword(this RouteGroupBuilder group) =>
        group.MapPost("/change-password", async (
                ChangePasswordRequest req, ClaimsPrincipal principal,
                UserManager<AppUser> users, SessionService sessions, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
                return ProblemResults.From(AuthErrors.UserGone);
            Guid.TryParse(principal.FindFirstValue("sid"), out var currentFamily);

            var result = await ChangeAsync(users, sessions, userId, currentFamily, req, ct);
            return result.IsError ? ProblemResults.From(result.Errors) : TypedResults.NoContent();
        })
        .RequireAuthorization()
        .WithName("ChangePassword")
        .WithSummary("Change the signed-in user's password; revokes the user's other sessions.")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);

    private static async Task<ErrorOr<Success>> ChangeAsync(
        UserManager<AppUser> users, SessionService sessions,
        Guid userId, Guid currentFamily, ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return AuthErrors.UserGone;

        IdentityResult result;
        if (user.MustChangePassword)
        {
            // Forced change: the user just authenticated at login, so no current password.
            var token = await users.GeneratePasswordResetTokenAsync(user);
            result = await users.ResetPasswordAsync(user, token, req.NewPassword);
        }
        else
        {
            if (string.IsNullOrEmpty(req.CurrentPassword)) return AuthErrors.CurrentPasswordRequired;
            result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        }

        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch")) return AuthErrors.WrongCurrentPassword;
            // Password-policy failures → validation errors, grouped by Identity's error code.
            return result.Errors.Select(e => Error.Validation($"auth.{e.Code}", e.Description)).ToList();
        }

        if (user.MustChangePassword)
        {
            user.MustChangePassword = false;
            await users.UpdateAsync(user);
        }
        // Security: a password change revokes the user's other sessions (keep the current device).
        await sessions.RevokeAllForUserAsync(user.Id, exceptFamilyId: currentFamily, ct);
        return Result.Success;
    }
}
