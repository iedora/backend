using System.ComponentModel.DataAnnotations;
using Framework.Web;
using Iedora.Api.Common;
using Iedora.Api.Features.Tenants;
using Iedora.Api.Observability;
using Iedora.Api.Security;
using Iedora.Api.Sessions;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Api.Features.Login;

public sealed record LoginRequest(
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password);

// POST /auth/login — verify the password with Identity, open a refresh-session family, set the
// refresh cookie, and mint a session-bound ES256 access token. Failure is an explicit
// AuthErrors value mapped to 401 (no exception/null). Custom span + counter ride the
// iedora-api ActivitySource/Meter.
public static class LoginEndpoint
{
    public static void MapLogin(this RouteGroupBuilder group) =>
        group.MapPost("/login", async (
                LoginRequest req, HttpContext http, UserManager<AppUser> users, ITenancyApi tenancy,
                SessionService sessions, JwtTokenService jwt, RefreshCookie cookie, CancellationToken ct) =>
        {
            using var activity = Telemetry.ActivitySource.StartActivity("auth.login");

            var user = await users.FindByEmailAsync(req.Email);
            if (user is null || !await users.CheckPasswordAsync(user, req.Password))
            {
                activity?.SetTag("auth.result", "denied");
                Telemetry.TokensIssued.Add(1, new("grant", "password"), new("result", "denied"));
                return ProblemResults.From(AuthErrors.InvalidCredentials);
            }

            var roles = await users.GetRolesAsync(user);
            // Pin the user's default tenant as the session's (null until they join/create one).
            // Cross-module read via the Tenancy public API — login never touches tenancy tables.
            var tenantId = await tenancy.GetDefaultTenantAsync(user.Id, ct);
            var (session, rawToken) = await sessions.CreateAsync(user.Id, tenantId, RequestMeta.From(http), ct);
            var response = AuthTokens.Issue(http.Response, user, roles, session, rawToken, jwt, cookie);

            activity?.SetTag("auth.result", "issued");
            activity?.SetTag("user.id", user.Id.ToString());
            Telemetry.TokensIssued.Add(1, new("grant", "password"), new("result", "issued"));
            return TypedResults.Ok(response);
        })
        .WithName("Login")
        .WithSummary("Authenticate with email + password; opens a session and returns an ES256 access token.")
        .Produces<TokenResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
