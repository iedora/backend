using System.Security.Claims;
using Iedora.Kernel;
using Iedora.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Identity;

public sealed record WhoAmIResponse(
    string UserId, string? TenantId, string[] Roles, string? Email, bool? MustChangePassword);

// GET /auth/whoami — the identity behind the bearer access token. `mustChangePassword` is read
// LIVE from the DB (not the token) so the client's guard stops redirecting the instant the user
// completes a forced change, without waiting for the token to expire.
public static class WhoAmIEndpoint
{
    public static void MapWhoAmI(this RouteGroupBuilder group) =>
        group.MapGet("/whoami",
            async Task<Results<Ok<WhoAmIResponse>, ProblemHttpResult>> (
                ClaimsPrincipal principal, IdentityDbContext db, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized);

            var mustChange = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => u.MustChangePassword)
                .FirstOrDefaultAsync(ct);

            return TypedResults.Ok(new WhoAmIResponse(
                userId.ToString(),
                principal.FindFirstValue("tid"),
                principal.FindAll("roles").Select(c => c.Value).ToArray(),
                principal.FindFirstValue("email"),
                mustChange ? true : null));
        })
        .RequireAuthorization()
        .WithName("WhoAmI")
        .WithSummary("Return the identity resolved from the bearer access token.");
}
