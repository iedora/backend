using System.Security.Claims;
using Iedora.Auth.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Iedora.Auth.Features.Logout;

public sealed record OkResponse(bool Ok);

// POST /auth/logout        — revoke this device's session family (idempotent; always 200).
// POST /auth/logout-all    — revoke every session for the signed-in user (bearer required).
public static class LogoutEndpoint
{
    public static void MapLogout(this RouteGroupBuilder group)
    {
        group.MapPost("/logout",
            async Task<Ok<OkResponse>> (
                HttpContext http, SessionService sessions, RefreshCookie cookie, CancellationToken ct) =>
        {
            var raw = cookie.Read(http.Request);
            if (raw is not null) await sessions.RevokeFamilyByTokenAsync(raw, ct);
            cookie.Clear(http.Response);
            return TypedResults.Ok(new OkResponse(true));
        })
        .AllowAnonymous()
        .WithName("Logout")
        .WithSummary("Revoke this device's session and clear the refresh cookie (idempotent).");

        group.MapPost("/logout-all",
            async Task<Results<Ok<OkResponse>, ProblemHttpResult>> (
                ClaimsPrincipal principal, HttpContext http, SessionService sessions, RefreshCookie cookie, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);
            await sessions.RevokeAllForUserAsync(userId, exceptFamilyId: null, ct);
            cookie.Clear(http.Response);
            return TypedResults.Ok(new OkResponse(true));
        })
        .RequireAuthorization()
        .WithName("LogoutAll")
        .WithSummary("Revoke all of the signed-in user's sessions (every device).");
    }
}
