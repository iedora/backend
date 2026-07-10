using System.Net;
using System.Net.Http.Headers;

namespace Iedora.Dashboard;

/// <summary>Attaches the admin's access token to API calls. On a 401 (token expired) it rotates the
/// token once via the API's refresh cookie and retries; if refresh fails, it signs the admin out so
/// the router bounces them to login.</summary>
public sealed class BearerHandler(TokenStore tokens, ApiAuthClient auth, ApiAuthStateProvider state) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Attach(request);
        var response = await base.SendAsync(request, ct);
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;

        response.Dispose();
        var refreshed = await auth.RefreshAsync(ct);
        if (refreshed is null)
        {
            await state.SignOutAsync();
            return await base.SendAsync(Clone(request), ct); // return the fresh 401; auth state is now anonymous
        }

        state.SignedIn(refreshed);
        var retry = Clone(request);
        Attach(retry);
        return await base.SendAsync(retry, ct);
    }

    private void Attach(HttpRequestMessage request)
    {
        if (tokens.AccessToken is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    // Requests can't be resent, so clone before the retry. Staff reads are GETs (no body); copy method,
    // uri and headers.
    private static HttpRequestMessage Clone(HttpRequestMessage request)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
        foreach (var header in request.Headers)
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options)
            ((IDictionary<string, object?>)copy.Options)[option.Key] = option.Value;
        return copy;
    }
}
