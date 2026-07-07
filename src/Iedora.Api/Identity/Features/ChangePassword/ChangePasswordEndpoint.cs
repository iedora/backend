using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Framework.Commands;
using Framework.Web;
using Iedora.Api.Identity;
using Iedora.Api.Security;
using Iedora.Api.Shared;
using Iedora.Data;
using Iedora.Messaging;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Api.Features.ChangePassword;

public sealed record ChangePasswordRequest(
    string? CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 8)] string NewPassword);

// POST /auth/change-password — change the signed-in user's password (async). Everything that can
// reject the request is checked synchronously first: a voluntary change verifies the current password
// (missing → 400, wrong → 403), a forced change (MustChangePassword) skips it, and the new password is
// run through the Identity policy (weak → 400). Only then is the hash computed and a command submitted
// → 202. The handler swaps the hash and revokes the user's OTHER sessions; poll the status URL.
public static class ChangePasswordEndpoint
{
    public static void MapChangePassword(this RouteGroupBuilder group) =>
        group.MapPost("/change-password", async (
                ChangePasswordRequest req, ClaimsPrincipal principal,
                UserManager<AppUser> users, IdentityDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return ProblemResults.From(CommonErrors.Unauthenticated);

            var user = await users.FindByIdAsync(userId.ToString());
            if (user is null) return ProblemResults.From(IdentityErrors.UserGone);

            // Forced change: the user just authenticated at login, so no current password is required.
            if (!user.MustChangePassword)
            {
                if (string.IsNullOrEmpty(req.CurrentPassword))
                    return ProblemResults.From(IdentityErrors.CurrentPasswordRequired);
                if (!await users.CheckPasswordAsync(user, req.CurrentPassword))
                    return ProblemResults.From(IdentityErrors.WrongCurrentPassword);
            }

            var policy = await PasswordPolicy.ValidateAsync(users, user, req.NewPassword);
            if (policy.IsError) return ProblemResults.From(policy.Errors);

            Guid.TryParse(principal.FindFirstValue("sid"), out var currentFamily);
            var passwordHash = users.PasswordHasher.HashPassword(user, req.NewPassword);
            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, ChangePasswordCommand.Type,
                new ChangePasswordCommand(userId, passwordHash, currentFamily), clock);
            await db.SaveChangesAsync(ct);

            return CommandEndpoints.AcceptedCommand(commandId, "/auth");
        })
        .RequireAuthorization()
        .WithName("ChangePassword")
        .WithSummary("Change the signed-in user's password (async — poll the status URL); revokes other sessions.")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status403Forbidden);
}
