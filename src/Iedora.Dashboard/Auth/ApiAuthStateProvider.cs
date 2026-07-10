using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Iedora.Dashboard;

/// <summary>The dashboard's authentication state, derived from the in-memory access token. On first
/// load with no token it attempts a silent refresh — the browser may still hold the API's refresh
/// cookie from a prior visit, so a reload restores the admin without re-login.</summary>
public sealed class ApiAuthStateProvider(TokenStore tokens, ApiAuthClient auth) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));
    private bool _triedSilentRefresh;

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (tokens.AccessToken is null && !_triedSilentRefresh)
        {
            _triedSilentRefresh = true;
            tokens.AccessToken = await auth.RefreshAsync(CancellationToken.None);
        }
        return tokens.AccessToken is { } token ? new AuthenticationState(Principal(token)) : Anonymous;
    }

    /// <summary>Adopt a freshly issued token (login or a mid-request refresh) and re-render auth-aware UI.</summary>
    public void SignedIn(string accessToken)
    {
        tokens.AccessToken = accessToken;
        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(Principal(accessToken))));
    }

    /// <summary>Clear the local token, revoke the session at the API, and drop to anonymous.</summary>
    public async Task SignOutAsync()
    {
        tokens.AccessToken = null;
        await auth.LogoutAsync(CancellationToken.None);
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    private static ClaimsPrincipal Principal(string accessToken) =>
        new(new ClaimsIdentity(Jwt.ReadClaims(accessToken), "jwt", ClaimTypes.Name, ClaimTypes.Role));
}
