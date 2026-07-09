namespace Iedora.Dashboard;

/// <summary>Request/circuit-scoped holder for the admin's API access token. Set directly during login
/// (before the cookie exists, so the whoami round-trip is authenticated); on later requests the token
/// rides the auth cookie as the <see cref="ClaimType"/> claim and <see cref="BearerHandler"/> reads it
/// from the current authentication state.</summary>
public sealed class AccessToken
{
    /// <summary>Cookie claim carrying the API access token for the signed-in admin.</summary>
    public const string ClaimType = "access_token";

    /// <summary>Cookie claim carrying the raw API refresh-cookie value, used to rotate the access token
    /// (kept server-side in the dashboard's own auth cookie, never exposed to the browser as a token).</summary>
    public const string RefreshClaimType = "refresh_token";

    /// <summary>Cookie claim carrying the access token's expiry (ISO-8601), so the refresh hook knows
    /// when to rotate.</summary>
    public const string ExpiresClaimType = "access_expires";

    public string? Value { get; set; }
}
