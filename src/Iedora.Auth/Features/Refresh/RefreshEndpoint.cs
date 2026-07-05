using Iedora.Auth.Data;
using Iedora.Auth.Security;
using Iedora.Auth.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.Refresh;

// POST /auth/refresh — rotate the refresh token from the cookie. Authenticated by the cookie,
// not a bearer token. A spent/expired/reused token yields 401 (and reuse burns the family).
public static class RefreshEndpoint
{
    public static void MapRefresh(this RouteGroupBuilder group) =>
        group.MapPost("/refresh",
            async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> (
                HttpContext http, SessionService sessions, UserManager<AppUser> users,
                JwtTokenService jwt, RefreshCookie cookie, CancellationToken ct) =>
        {
            var raw = cookie.Read(http.Request);
            if (raw is null)
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "no refresh token");

            var rotated = await sessions.RotateAsync(raw, RequestMeta.From(http), ct);
            if (rotated is null)
            {
                cookie.Clear(http.Response);
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "invalid refresh token");
            }

            var roles = await users.GetRolesAsync(rotated.User);
            var response = AuthTokens.Issue(http.Response, rotated.User, roles, rotated.Session, rotated.Token, jwt, cookie);
            return TypedResults.Ok(response);
        })
        .AllowAnonymous()
        .WithName("Refresh")
        .WithSummary("Rotate the refresh-token cookie and issue a fresh access token.")
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
