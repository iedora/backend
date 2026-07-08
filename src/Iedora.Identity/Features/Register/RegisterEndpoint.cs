using System.ComponentModel.DataAnnotations;
using Framework.Commands;
using Framework.Web;
using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

public sealed record RegisterRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required, StringLength(128, MinimumLength = 8)] string Password,
    string? DisplayName);

// POST /auth/register — validate synchronously (incl. a sync email-taken pre-check → 409), hash the
// password at the boundary (only the hash travels through the command/outbox tables), then submit →
// 202. RegisterUserHandler creates the account off the outbox. Poll the returned status URL.
public static class RegisterEndpoint
{
    public static void MapRegister(this RouteGroupBuilder group) =>
        group.MapPost("/register", async (
                RegisterRequest req, IdentityDbContext db, UserManager<AppUser> users,
                IPasswordHasher<AppUser> hasher, TimeProvider clock, CancellationToken ct) =>
        {
            if (await users.FindByEmailAsync(req.Email) is not null)
            {
                Telemetry.Registrations.Add(1, new KeyValuePair<string, object?>(Telemetry.Tags.Result, Telemetry.Result.Rejected));
                return ProblemResults.From(IdentityErrors.EmailTaken);
            }

            var passwordHash = hasher.HashPassword(new AppUser(), req.Password);
            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, RegisterUserCommand.Type,
                new RegisterUserCommand(req.Email, passwordHash, req.DisplayName), clock);
            await db.SaveChangesAsync(ct);

            return CommandEndpoints.AcceptedCommand(commandId, "/auth");
        })
        .WithName("Register")
        .WithSummary("Create a new account (async — poll the status URL).")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status409Conflict);
}
