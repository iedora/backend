namespace Iedora.Dashboard;

/// <summary>Request/circuit-scoped holder for the admin's API access token. Set directly during login
/// (before the cookie exists, so the whoami round-trip is authenticated); on later requests the token
/// rides as the <see cref="ClaimType"/> claim in the auth cookie and the <see cref="BearerHandler"/>
/// reads it from the current authentication state.</summary>
public sealed class AccessToken
{
    /// <summary>Cookie claim that carries the API access token for the signed-in admin.</summary>
    public const string ClaimType = "access_token";

    public string? Value { get; set; }
}
