using System.ComponentModel.DataAnnotations;
using Framework.Commands;
using Framework.Web;
using Iedora.Kernel;
using Iedora.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

public sealed record AdminSetPasswordRequest(
    [property: Required, StringLength(128, MinimumLength = 8)] string Password);

// Service-only user administration for the menu BFF's staff "Users" CRM. Gated by the Service policy
// (an internal service token from POST /auth/token). Device history is a synchronous read; kicking a
// device is a synchronous session-plane op (like /auth/logout); the password actions are async writes
// on the user aggregate (submitted as commands, drained by the worker — poll the status URL).
//   GET  /auth/admin/users/{id}/sessions                 — that user's device history
//   POST /auth/admin/users/{id}/sessions/{family}/revoke — kick one device
//   POST /auth/admin/users/{id}/force-password-change    — force a change at next login
//   POST /auth/admin/users/{id}/set-password             — set a temporary password
public static class UsersAdminEndpoints
{
    private const int MaxSessions = 50;

    public static void MapUsersAdmin(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/users").RequireAuthorization(Policies.Service);

        admin.MapGet("/{id:guid}/sessions",
            async Task<Results<Ok<SessionsResponse>, NotFound>> (
                Guid id, UserManager<AppUser> users, SessionService sessions, TimeProvider clock, CancellationToken ct) =>
        {
            if (await users.FindByIdAsync(id.ToString()) is null) return TypedResults.NotFound();

            var now = clock.GetUtcNow();
            var rows = await sessions.ListForUserAsync(id, MaxSessions, ct);
            return TypedResults.Ok(new SessionsResponse(rows.Select(s => SessionView.From(s, now)).ToList()));
        })
        .WithName("AdminListUserSessions")
        .WithSummary("A user's sessions / device history (service).");

        admin.MapPost("/{id:guid}/sessions/{family:guid}/revoke",
            async Task<Results<Ok<OkResponse>, NotFound>> (
                Guid id, Guid family, SessionService sessions, CancellationToken ct) =>
        {
            // Owner-scoped to this user: an unknown/dead family (or one that isn't theirs) → 404.
            return await sessions.RevokeFamilyForUserAsync(id, family, ct)
                ? TypedResults.Ok(new OkResponse(true))
                : TypedResults.NotFound();
        })
        .WithName("AdminRevokeUserSession")
        .WithSummary("Kick one of a user's devices (service).");

        admin.MapPost("/{id:guid}/force-password-change",
            async Task<Results<Accepted<CommandAccepted>, NotFound>> (
                Guid id, UserManager<AppUser> users, IdentityDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (await users.FindByIdAsync(id.ToString()) is null) return TypedResults.NotFound();

            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, ForcePasswordChangeCommand.Type, new ForcePasswordChangeCommand(id), clock);
            await db.SaveChangesAsync(ct);
            return CommandEndpoints.AcceptedCommand(commandId, "/auth");
        })
        .WithName("AdminForcePasswordChange")
        .WithSummary("Force a user to change their password at next login (service, async — poll the status URL).")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status404NotFound);

        admin.MapPost("/{id:guid}/set-password", async (
                Guid id, AdminSetPasswordRequest req, UserManager<AppUser> users, IdentityDbContext db,
                TimeProvider clock, CancellationToken ct) =>
        {
            var user = await users.FindByIdAsync(id.ToString());
            if (user is null) return Results.NotFound();

            var policy = await PasswordPolicy.ValidateAsync(users, user, req.Password);
            if (policy.IsError) return ProblemResults.From(policy.Errors);

            var passwordHash = users.PasswordHasher.HashPassword(user, req.Password);
            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, SetUserPasswordCommand.Type,
                new SetUserPasswordCommand(id, passwordHash), clock);
            await db.SaveChangesAsync(ct);
            return CommandEndpoints.AcceptedCommand(commandId, "/auth");
        })
        .WithName("AdminSetUserPassword")
        .WithSummary("Set a temporary password a user must change at next login (service, async — poll the status URL).")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
