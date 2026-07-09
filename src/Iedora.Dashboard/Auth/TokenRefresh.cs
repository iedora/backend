using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;

namespace Iedora.Dashboard;

/// <summary>Keeps the admin's API access token fresh. The dashboard cookie carries the access token,
/// the (rotating) refresh-cookie value, and the access token's expiry; on each request this hook
/// rotates the token via the API's <c>/auth/refresh</c> shortly before it expires and renews the
/// dashboard cookie — so SSR page loads always call the API with a live token.</summary>
public static class TokenRefresh
{
    // Rotate a little early, so a request that starts just before expiry still carries a live token.
    private static readonly TimeSpan Skew = TimeSpan.FromMinutes(2);

    /// <summary>Build the cookie principal's claims from an auth result — shared by login and refresh so
    /// the identity is shaped identically.</summary>
    public static List<Claim> BuildClaims(string userId, string? name, IEnumerable<string> roles, AuthResult auth)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, name ?? userId),
            new(AccessToken.ClaimType, auth.AccessToken),
            new(AccessToken.RefreshClaimType, auth.RefreshToken),
            new(AccessToken.ExpiresClaimType, auth.ExpiresAt.ToString("o")),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return claims;
    }

    public static async Task OnValidatePrincipalAsync(CookieValidatePrincipalContext ctx)
    {
        var user = ctx.Principal;
        if (user?.FindFirst(AccessToken.ExpiresClaimType)?.Value is not { } expiresRaw
            || user.FindFirst(AccessToken.RefreshClaimType)?.Value is not { } refreshToken
            || !DateTimeOffset.TryParse(expiresRaw, out var expiresAt))
            return;

        var clock = ctx.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        if (clock.GetUtcNow() < expiresAt - Skew) return; // still fresh — nothing to do

        var result = await ctx.HttpContext.RequestServices.GetRequiredService<AuthApi>()
            .RefreshAsync(refreshToken, ctx.HttpContext.RequestAborted);
        if (result is null)
        {
            // Refresh failed (expired / reuse-burned) — drop the session so the admin re-authenticates.
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var claims = BuildClaims(
            user.FindFirst(ClaimTypes.NameIdentifier)!.Value,
            user.FindFirst(ClaimTypes.Name)?.Value,
            user.FindAll(ClaimTypes.Role).Select(c => c.Value),
            result);
        ctx.ReplacePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, ctx.Scheme.Name)));
        ctx.ShouldRenew = true; // re-issue the dashboard cookie with the rotated tokens
    }
}
