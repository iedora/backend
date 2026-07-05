using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Iedora.Auth.IntegrationTests;

// Wire payloads (camelCase, as ASP.NET serializes them).
public sealed record TokenPayload(string accessToken, string expiresAt, string userId, string? tenantId, bool? mustChangePassword);
public sealed record WhoAmiPayload(string userId, string? tenantId, string[] roles, string? email, bool? mustChangePassword);

[CollectionDefinition(nameof(AuthCollection))]
public sealed class AuthCollection : ICollectionFixture<AuthApiFactory>;

/// <summary>
/// Base for integration tests: a fresh DB per test (Respawn) and a cookie-less HTTP client, so
/// each test controls exactly which refresh token it presents (essential for reuse scenarios).
/// </summary>
[Collection(nameof(AuthCollection))]
public abstract class IntegrationTest(AuthApiFactory factory) : IAsyncLifetime
{
    private const string RefreshCookie = "iedora_refresh";
    protected readonly HttpClient Client =
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    public async ValueTask InitializeAsync() => await factory.ResetDatabaseAsync();
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    protected Task<HttpResponseMessage> Register(string email, string password, string? name = "User") =>
        Client.PostAsJsonAsync("/auth/register", new { email, password, displayName = name });

    /// <summary>Log in and assert success; returns the token body + the raw refresh cookie.</summary>
    protected async Task<(TokenPayload body, string refresh)> Login(string email, string password)
    {
        var resp = await Client.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<TokenPayload>())!;
        return (body, RefreshCookieFrom(resp)!);
    }

    protected Task<HttpResponseMessage> Refresh(string refreshToken) => Post("/auth/refresh", refreshToken);
    protected Task<HttpResponseMessage> Logout(string refreshToken) => Post("/auth/logout", refreshToken);

    protected Task<HttpResponseMessage> Post(string path, string? refreshToken = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        if (refreshToken is not null) req.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");
        return Client.SendAsync(req);
    }

    protected Task<HttpResponseMessage> Get(string path, string? bearer = null) =>
        Send(HttpMethod.Get, path, bearer);

    protected Task<HttpResponseMessage> PostBearer(string path, string? bearer) =>
        Send(HttpMethod.Post, path, bearer);

    private Task<HttpResponseMessage> Send(HttpMethod method, string path, string? bearer)
    {
        var req = new HttpRequestMessage(method, path);
        if (bearer is not null) req.Headers.Authorization = new("Bearer", bearer);
        return Client.SendAsync(req);
    }

    protected async Task<TokenPayload> RegisterAndLogin(string email, string password)
    {
        Assert.Equal(System.Net.HttpStatusCode.Created, (await Register(email, password)).StatusCode);
        return (await Login(email, password)).body;
    }

    /// <summary>The refresh token value from a Set-Cookie header, or null if none was set.</summary>
    protected static string? RefreshCookieFrom(HttpResponseMessage resp)
    {
        if (!resp.Headers.TryGetValues("Set-Cookie", out var cookies)) return null;
        foreach (var c in cookies)
        {
            if (!c.StartsWith($"{RefreshCookie}=")) continue;
            var value = c[$"{RefreshCookie}=".Length..].Split(';')[0];
            return string.IsNullOrEmpty(value) ? null : value; // empty ⇒ cleared
        }
        return null;
    }
}
