
namespace Iedora.Identity;

/// <summary>
/// The token response shared by login + refresh — matches the frontend's decoded shape
/// (<c>accessToken</c>, <c>expiresAt</c> = access-token expiry, <c>userId</c>, optional
/// <c>tenantId</c> / <c>mustChangePassword</c>).
/// </summary>
public sealed record TokenResponse(
    string AccessToken, string ExpiresAt, string UserId, string? TenantId, bool? MustChangePassword);

/// <summary>Mints the access token, sets the refresh cookie, and shapes the response — the
/// step login and refresh perform identically once they have a user + fresh session.</summary>
public static class IdentityTokens
{
    public static TokenResponse Issue(
        HttpResponse response, AppUser user, IEnumerable<string> roles, Session session,
        string rawToken, JwtTokenService jwt, RefreshCookie cookie)
    {
        var (accessToken, expiresAt) =
            jwt.Issue(user, roles, session.FamilyId, session.TenantId, user.MustChangePassword);
        cookie.Set(response, rawToken, session.ExpiresAt);
        return new TokenResponse(
            accessToken,
            expiresAt.ToString("o"),
            user.Id.ToString(),
            session.TenantId?.ToString(),
            user.MustChangePassword ? true : null);
    }
}
