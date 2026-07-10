namespace Iedora.Dashboard;

/// <summary>Holds the current admin's API access token in memory (per browser tab). The refresh token
/// never lives here — the browser holds it as the API's HttpOnly cookie, out of reach of our code.</summary>
public sealed class TokenStore
{
    public string? AccessToken { get; set; }
}
