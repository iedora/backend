using System.ComponentModel.DataAnnotations;
using Iedora.Auth.Data;
using Iedora.Auth.Observability;
using Iedora.Auth.Security;
using Iedora.Auth.Sessions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.Login;

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

// POST /auth/login — verify the password with Identity, open a refresh-session family, set the
// refresh cookie, and mint a session-bound ES256 access token. Custom span + counter ride the
// iedora-auth ActivitySource/Meter.
public static class LoginEndpoint
{
    public static void MapLogin(this RouteGroupBuilder group) =>
        group.MapPost("/login",
            async Task<Results<Ok<TokenResponse>, ProblemHttpResult>> (
                LoginRequest req, HttpContext http, UserManager<AppUser> users,
                SessionService sessions, JwtTokenService jwt, RefreshCookie cookie, CancellationToken ct) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.login");

            var user = await users.FindByEmailAsync(req.Email);
            if (user is null || !await users.CheckPasswordAsync(user, req.Password))
            {
                activity?.SetTag("auth.result", "denied");
                Telemetry.TokensIssued.Add(1, new("grant", "password"), new("result", "denied"));
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, detail: "invalid credentials");
            }

            var roles = await users.GetRolesAsync(user);
            var (session, rawToken) = await sessions.CreateAsync(user.Id, tenantId: null, RequestMeta.From(http), ct);
            var response = AuthTokens.Issue(http.Response, user, roles, session, rawToken, jwt, cookie);

            activity?.SetTag("auth.result", "issued");
            activity?.SetTag("user.id", user.Id.ToString());
            Telemetry.TokensIssued.Add(1, new("grant", "password"), new("result", "issued"));
            return TypedResults.Ok(response);
        })
        .WithName("Login")
        .WithSummary("Authenticate with email + password; opens a session and returns an ES256 access token.")
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
