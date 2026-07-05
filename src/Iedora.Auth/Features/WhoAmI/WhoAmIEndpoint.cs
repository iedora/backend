using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Iedora.Auth.Features.WhoAmI;

public sealed record WhoAmIResponse(string? Sub, string? Email, string[] Roles);

// GET /auth/whoami — protected by the built-in JwtBearer handler via RequireAuthorization().
// Echoes the identity the framework resolved from the ES256 access token.
public static class WhoAmIEndpoint
{
    public static void MapWhoAmI(this RouteGroupBuilder group) =>
        group.MapGet("/whoami", Ok<WhoAmIResponse> (ClaimsPrincipal user) => TypedResults.Ok(
            new WhoAmIResponse(
                user.FindFirstValue("sub"),
                user.FindFirstValue("email"),
                user.FindAll("roles").Select(c => c.Value).ToArray())))
        .RequireAuthorization()
        .WithName("WhoAmI")
        .WithSummary("Return the identity resolved from the bearer access token.");
}
