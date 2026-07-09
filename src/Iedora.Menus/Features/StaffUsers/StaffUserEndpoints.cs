using System.ComponentModel.DataAnnotations;
using Framework.Web;
using Iedora.Identity.Contracts;
using Iedora.Tenancy.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Iedora.Menus;

// The staff Users CRM view: a user's admin record + how many tenants they're in.
public sealed record AdminUserView(
    Guid Id, string Email, string? Name, IReadOnlyList<string> Roles, DateTimeOffset CreatedAt,
    DateTimeOffset? PasswordChangedAt, bool EmailConfirmed, bool MustChangePassword, int TenantCount)
{
    public static AdminUserView From(AdminUserRecord u, int tenantCount) => new(
        u.Id, u.Email, u.Name, u.Roles, u.CreatedAt, u.PasswordChangedAt, u.EmailConfirmed, u.MustChangePassword, tenantCount);
}

public sealed record StaffUserListResponse(IReadOnlyList<AdminUserView> Users);
public sealed record StaffUserDetail(
    AdminUserView User, IReadOnlyList<MembershipRecord> Memberships, IReadOnlyList<UserSessionRecord> Sessions);

public sealed record SetUserPasswordRequest([property: Required, MinLength(1)] string Password);

// Cross-tenant Users CRM under /api/staff/users (platform admin). The menu module is the BFF: it
// fans out to Identity (users, sessions, credential actions) + Tenancy (memberships) in-process and
// composes. The audit timeline (what a user DID) is a separate concern (deferred with audit).
public static class StaffUserEndpoints
{
    public static void MapStaffUsers(this RouteGroupBuilder staff)
    {
        var users = staff.MapGroup("/users");

        // GET /api/staff/users?q=&limit= — search users (email/name), newest first, each with tenant count.
        users.MapGet("/", async (
                string? q, int? limit, IIdentityApi identity, ITenancyApi tenancy, CancellationToken ct) =>
        {
            var rows = await identity.ListUsersAsync(q, limit ?? Paging.DefaultLimit, ct);
            var counts = await tenancy.TenantCountsAsync(rows.Select(u => u.Id).ToList(), ct);
            var views = rows.Select(u => AdminUserView.From(u, counts.GetValueOrDefault(u.Id))).ToList();
            return TypedResults.Ok(new StaffUserListResponse(views));
        })
        .WithName("StaffListUsers").WithSummary("Search/list users for the staff CRM.");

        // GET /api/staff/users/{id} — one user + tenant memberships + device/session history.
        users.MapGet("/{id:guid}",
            async Task<Results<Ok<StaffUserDetail>, NotFound>> (
                Guid id, IIdentityApi identity, ITenancyApi tenancy, CancellationToken ct) =>
        {
            var user = await identity.GetUserAsync(id, ct);
            if (user is null) return TypedResults.NotFound();

            var memberships = await tenancy.MembershipsForUserAsync(id, ct);
            var sessions = await identity.ListUserSessionsAsync(id, ct);
            return TypedResults.Ok(new StaffUserDetail(AdminUserView.From(user, memberships.Count), memberships, sessions));
        })
        .WithName("StaffUserDetail").WithSummary("A user's record + tenant memberships + sessions (staff).");

        // POST /api/staff/users/{id}/force-password-change — require a change at next login + sign out.
        users.MapPost("/{id:guid}/force-password-change",
            async Task<Results<NoContent, NotFound>> (Guid id, IIdentityApi identity, CancellationToken ct) =>
            await identity.ForcePasswordChangeAsync(id, ct) ? TypedResults.NoContent() : TypedResults.NotFound())
        .WithName("StaffForcePasswordChange").WithSummary("Force a password change at next login (staff).");

        // POST /api/staff/users/{id}/set-password — set a temporary password (forces a change + signs out).
        users.MapPost("/{id:guid}/set-password", async (
                Guid id, SetUserPasswordRequest req, IIdentityApi identity, CancellationToken ct) =>
            await identity.SetUserPasswordAsync(id, req.Password, ct) switch
            {
                UserPasswordOutcome.Updated => Results.NoContent(),
                UserPasswordOutcome.UserNotFound => Results.NotFound(),
                _ => Results.Problem("The password does not meet the requirements.", statusCode: StatusCodes.Status400BadRequest),
            })
        .WithName("StaffSetUserPassword").WithSummary("Set a temporary password for a user (staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/staff/users/{id}/sessions/{family}/revoke — sign out one of a user's devices.
        users.MapPost("/{id:guid}/sessions/{family:guid}/revoke",
            async Task<Results<NoContent, NotFound>> (
                Guid id, Guid family, IIdentityApi identity, CancellationToken ct) =>
            await identity.RevokeUserSessionAsync(id, family, ct) ? TypedResults.NoContent() : TypedResults.NotFound())
        .WithName("StaffRevokeUserSession").WithSummary("Revoke one of a user's sessions (staff).");
    }
}
