using System.Net.Http.Json;
using Iedora.Dashboard.Api;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace Iedora.Dashboard;

/// <summary>Talks to the API's <c>/auth/login</c>, <c>/auth/refresh</c> and <c>/auth/logout</c> with
/// browser credentials, so the API's HttpOnly refresh cookie is stored and replayed by the browser —
/// our code never sees it. Returns the access token (which the caller keeps in memory).</summary>
public sealed class ApiAuthClient(IHttpClientFactory factory)
{
    private HttpClient Http => factory.CreateClient("auth");

    public Task<string?> LoginAsync(string email, string password, CancellationToken ct)
    {
        var request = WithCredentials(new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = email, Password = password }),
        });
        return SendForTokenAsync(request, ct);
    }

    public Task<string?> RefreshAsync(CancellationToken ct) =>
        SendForTokenAsync(WithCredentials(new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")), ct);

    public async Task LogoutAsync(CancellationToken ct)
    {
        try { using var _ = await Http.SendAsync(WithCredentials(new(HttpMethod.Post, "/auth/logout")), ct); }
        catch { /* best-effort — the local token is cleared regardless */ }
    }

    private static HttpRequestMessage WithCredentials(HttpRequestMessage request)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return request;
    }

    private async Task<string?> SendForTokenAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await Http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null; // bad credentials / expired / reuse-burned
            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
            return body?.AccessToken;
        }
        catch (HttpRequestException)
        {
            return null; // API unreachable / CORS / network — treat as "not signed in", never crash the app
        }
    }
}
