using Iedora.Auth.Data;
using Iedora.Auth.Observability;
using Iedora.Auth.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Features.Login;

public sealed record LoginRequest(string Email, string Password);
public sealed record LoginResponse(string AccessToken, string ExpiresAt, string UserId);

// POST /auth/login — verify the password with Identity, mint an ES256 JWT. Typed results
// so the OpenAPI schema (→ generated frontend client) is exact. Custom business span +
// counter ride the iedora-auth ActivitySource/Meter (the "proper OTel way").
public static class LoginEndpoint
{
    public static void MapLogin(this RouteGroupBuilder group) =>
        group.MapPost("/login",
            async Task<Results<Ok<LoginResponse>, ProblemHttpResult>> (
                LoginRequest req, UserManager<AppUser> users, JwtTokenService jwt) =>
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
            var (token, expiresAt) = jwt.Issue(user, roles);
            activity?.SetTag("auth.result", "issued");
            activity?.SetTag("user.id", user.Id.ToString());
            Telemetry.TokensIssued.Add(1, new("grant", "password"), new("result", "issued"));

            return TypedResults.Ok(new LoginResponse(token, expiresAt.ToString("o"), user.Id.ToString()));
        })
        .WithName("Login")
        .WithSummary("Authenticate with email + password and receive an ES256 access token.")
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
