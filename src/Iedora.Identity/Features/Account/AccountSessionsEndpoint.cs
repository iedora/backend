using System.Security.Claims;
using Iedora.Kernel;
using Iedora.Data;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Iedora.Identity;

/// <summary>One row of the caller's device history. <c>Current</c> means the session is still live
/// (usable for refresh) — revoked/expired rows are listed too, but flagged false. Never carries the
/// token hash.</summary>
public sealed record SessionView(
    Guid Id, Guid FamilyId, Guid? TenantId, string? Ip, string? UserAgent,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt, DateTimeOffset AbsoluteExpiresAt,
    DateTimeOffset? RevokedAt, bool Current)
{
    /// <summary>Project a session row to the wire view; <c>Current</c> = live at <paramref name="now"/>.</summary>
    public static SessionView From(Session s, DateTimeOffset now) =>
        new(s.Id, s.FamilyId, s.TenantId, s.Ip, s.UserAgent,
            s.IssuedAt, s.ExpiresAt, s.AbsoluteExpiresAt, s.RevokedAt, s.IsLive(now));
}

public sealed record SessionsResponse(IReadOnlyList<SessionView> Sessions);

// Self-service account security (the owner managing THEIR OWN devices from Settings). These are
// session-plane operations — synchronous, like /auth/logout — not domain writes, so no command.
//   GET  /auth/sessions                — my logged-in devices (full history)
//   POST /auth/sessions/{family}/revoke — sign out one device (scoped to me → 404 if not mine)
//   POST /auth/sessions/revoke-others  — sign out every other device (keep this one)
public static class AccountSessionsEndpoint
{
    private const int MaxSessions = 50;

    public static void MapAccountSessions(this RouteGroupBuilder group)
    {
        group.MapGet("/sessions",
            async Task<Results<Ok<SessionsResponse>, ProblemHttpResult>> (
                ClaimsPrincipal principal, SessionService sessions, TimeProvider clock, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);

            var now = clock.GetUtcNow();
            var rows = await sessions.ListForUserAsync(userId, MaxSessions, ct);
            return TypedResults.Ok(new SessionsResponse(rows.Select(s => SessionView.From(s, now)).ToList()));
        })
        .RequireAuthorization()
        .WithName("ListSessions")
        .WithSummary("List the signed-in user's sessions (their devices, full history).");

        group.MapPost("/sessions/{family:guid}/revoke",
            async Task<Results<Ok<OkResponse>, NotFound, ProblemHttpResult>> (
                Guid family, ClaimsPrincipal principal, SessionService sessions, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);

            return await sessions.RevokeFamilyForUserAsync(userId, family, ct)
                ? TypedResults.Ok(new OkResponse(true))
                : TypedResults.NotFound();
        })
        .RequireAuthorization()
        .WithName("RevokeSession")
        .WithSummary("Sign out one of the signed-in user's devices.");

        group.MapPost("/sessions/revoke-others",
            async Task<Results<Ok<OkResponse>, ProblemHttpResult>> (
                ClaimsPrincipal principal, SessionService sessions, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);

            // Keep the caller's own family (the "sid" claim) alive; revoke every other device.
            Guid? keep = Guid.TryParse(principal.FindFirstValue("sid"), out var family) ? family : null;
            await sessions.RevokeAllForUserAsync(userId, exceptFamilyId: keep, ct);
            return TypedResults.Ok(new OkResponse(true));
        })
        .RequireAuthorization()
        .WithName("RevokeOtherSessions")
        .WithSummary("Sign out every other device of the signed-in user (keep this one).");
    }
}
