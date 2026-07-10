using System.Net.Http.Json;
using Iedora.Dashboard.Api;
using Microsoft.Extensions.Options;

namespace Iedora.Dashboard;

/// <summary>Config for the dashboard's raw auth calls to the API.</summary>
public sealed class ApiAuthOptions
{
    public const string SectionName = "Api";

    /// <summary>The API's HttpOnly refresh-cookie name — must match IdentityService's
    /// <c>SessionSettings.RefreshCookieName</c>.</summary>
    public string RefreshCookieName { get; set; } = "iedora_refresh";
}

/// <summary>A login/refresh outcome: the fresh access token, its expiry, and the rotated raw
/// refresh-cookie value the dashboard stores (server-side) to refresh again later.</summary>
public sealed record AuthResult(string AccessToken, DateTimeOffset ExpiresAt, string RefreshToken);

/// <summary>
/// Calls the API's <c>/auth/login</c> and <c>/auth/refresh</c> with a raw HttpClient configured
/// <c>UseCookies=false</c>, so the HttpOnly refresh cookie is visible: read from <c>Set-Cookie</c> on
/// the way in, replayed as a <c>Cookie</c> header on the way out. The dashboard tracks that value
/// per-admin in its own auth cookie — never a shared cookie jar (which would leak across users).
/// </summary>
public sealed class AuthApi(HttpClient http, IOptions<ApiAuthOptions> options)
{
    private readonly string _refreshCookie = options.Value.RefreshCookieName;

    public Task<AuthResult?> LoginAsync(string email, string password, CancellationToken ct) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest { Email = email, Password = password }),
        }, ct);

    public Task<AuthResult?> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh");
        request.Headers.Add("Cookie", $"{_refreshCookie}={refreshToken}");
        return SendAsync(request, ct);
    }

    private async Task<AuthResult?> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null; // bad credentials / expired / reuse-burned

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        var refresh = ReadRefreshCookie(response);
        if (body?.AccessToken is null || refresh is null || !DateTimeOffset.TryParse(body.ExpiresAt, out var expiresAt))
            return null;
        return new AuthResult(body.AccessToken, expiresAt, refresh);
    }

    // Pull the rotated refresh-token value out of the response's Set-Cookie header.
    private string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        var prefix = _refreshCookie + "=";
        foreach (var cookie in cookies)
            if (cookie.StartsWith(prefix, StringComparison.Ordinal))
                return cookie[prefix.Length..].Split(';', 2)[0];
        return null;
    }
}
