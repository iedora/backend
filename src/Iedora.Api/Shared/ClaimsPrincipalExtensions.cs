using System.Security.Claims;

namespace Iedora.Api.Shared;

/// <summary>Cross-cutting helpers for the authenticated caller — shared by every module's authed
/// endpoints so they don't each re-read raw claims.</summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>The caller's user id from the access token's <c>sub</c> claim (JwtBearer runs with
    /// <c>MapInboundClaims = false</c>, so it stays "sub"). False when absent or unparseable.</summary>
    public static bool TryGetUserId(this ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue("sub"), out userId);
}
