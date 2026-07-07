using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// Wire payloads (camelCase, as ASP.NET serializes them).
public sealed record TokenPayload(string accessToken, string expiresAt, string userId, string? tenantId, bool? mustChangePassword);
public sealed record WhoAmiPayload(string userId, string? tenantId, string[] roles, string? email, bool? mustChangePassword);
public sealed record CommandAcceptedPayload(string commandId, string statusUrl);
public sealed record CommandStatusPayload(string id, string status, string? errorCode, string? resultLocation);

/// <summary>
/// Base for integration tests: a fresh DB per test (Respawn, via <c>[TestInitialize]</c>) and a
/// cookie-less HTTP client, so each test controls exactly which refresh token it presents
/// (essential for reuse scenarios). Integration tests run serially (MSTest's default) — they
/// share one container + Respawn-reset, so parallelism would race on the database.
/// </summary>
public abstract class IntegrationTestBase
{
    private const string RefreshCookie = "iedora_refresh";
    protected HttpClient Client = null!;
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public async Task InitializeTest()
    {
        await TestHost.ResetAsync();
        TestHost.EmailSender.Clear();
        Client = TestHost.Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    protected Task<HttpResponseMessage> Register(string email, string password, string? name = "User") =>
        Client.PostAsJsonAsync("/auth/register", new { email, password, displayName = name });

    /// <summary>Log in and assert success; returns the token body + the raw refresh cookie.</summary>
    protected async Task<(TokenPayload body, string refresh)> Login(string email, string password)
    {
        var resp = await Client.PostAsJsonAsync("/auth/login", new { email, password });
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
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

    protected Task<HttpResponseMessage> PostJson(string path, object body, string? bearer = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (bearer is not null) req.Headers.Authorization = new("Bearer", bearer);
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
        Assert.AreEqual(System.Net.HttpStatusCode.Created, (await Register(email, password)).StatusCode);
        return (await Login(email, password)).body;
    }

    /// <summary>Drive an accepted (202) Tenancy write end-to-end: dispatch the handler, poll the
    /// status, assert success, and return the resource id from the result location.</summary>
    protected async Task<string> AwaitTenancyCommandAsync(HttpResponseMessage accept, string bearer)
    {
        Assert.AreEqual(System.Net.HttpStatusCode.Accepted, accept.StatusCode);
        var accepted = (await accept.Content.ReadFromJsonAsync<CommandAcceptedPayload>())!;

        await TestHost.DispatchTenancyOutboxAsync();

        var status = await GetCommandStatus(accepted.statusUrl, bearer);
        Assert.AreEqual("Succeeded", status.status);
        return status.resultLocation!.Split('/')[^1]; // /tenancy/tenants/{id}
    }

    /// <summary>The signed-in user creates a tenant (async), returning its id once it lands.</summary>
    protected async Task<string> CreateTenantAsync(string name, string bearer) =>
        await AwaitTenancyCommandAsync(await PostJson("/tenancy/tenants", new { name }, bearer), bearer);

    protected async Task<CommandStatusPayload> GetCommandStatus(string statusUrl, string bearer)
    {
        var resp = await Get(statusUrl, bearer);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<CommandStatusPayload>())!;
    }

    /// <summary>The refresh token value from a Set-Cookie header, or null if none/cleared.</summary>
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
