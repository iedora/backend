using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components.Authorization;

namespace Iedora.Dashboard;

/// <summary>Attaches the current admin's bearer token to outgoing API calls. During login the token is
/// seeded directly on <see cref="AccessToken"/> (the cookie doesn't exist yet); afterwards it is read
/// from the <see cref="AccessToken.ClaimType"/> claim on the signed-in admin's authentication state.</summary>
public sealed class BearerHandler(AccessToken login, AuthenticationStateProvider authState) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var bearer = login.Value ?? await TokenFromClaimAsync();
        if (bearer is { Length: > 0 })
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await base.SendAsync(request, ct);
    }

    private async Task<string?> TokenFromClaimAsync()
    {
        try
        {
            var state = await authState.GetAuthenticationStateAsync();
            return state.User.FindFirst(AccessToken.ClaimType)?.Value;
        }
        catch
        {
            return null; // no auth state in this scope (e.g. the pre-sign-in /auth/login call)
        }
    }
}
