using Iedora.Api.Common;
using Framework.Web;
using Iedora.Data;
using Iedora.Api.Security;
using Iedora.Api.Sessions;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Api.Features.Refresh;

// POST /auth/refresh — rotate the refresh token from the cookie. Authenticated by the cookie,
// not a bearer token. RotateAsync returns an ErrorOr; any error maps to 401 (with a distinct
// code — invalid vs reuse), and reuse has already burned the family.
public static class RefreshEndpoint
{
    public static void MapRefresh(this RouteGroupBuilder group) =>
        group.MapPost("/refresh", async (
                HttpContext http, SessionService sessions, UserManager<AppUser> users,
                JwtTokenService jwt, RefreshCookie cookie, CancellationToken ct) =>
        {
            var raw = cookie.Read(http.Request);
            if (raw is null) return ProblemResults.From(AuthErrors.NoRefreshToken);

            var result = await sessions.RotateAsync(raw, RequestMeta.From(http), ct);
            if (result.IsError)
            {
                cookie.Clear(http.Response);
                return ProblemResults.From(result.Errors);
            }

            var rotated = result.Value;
            var roles = await users.GetRolesAsync(rotated.User);
            return TypedResults.Ok(AuthTokens.Issue(http.Response, rotated.User, roles, rotated.Session, rotated.Token, jwt, cookie));
        })
        .AllowAnonymous()
        .WithName("Refresh")
        .WithSummary("Rotate the refresh-token cookie and issue a fresh access token.")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
