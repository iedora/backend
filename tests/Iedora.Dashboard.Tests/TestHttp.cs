using System.Net;
using System.Net.Http.Json;

namespace Iedora.Dashboard.Tests;

// Shared HTTP test doubles for the dashboard's client-side auth.
internal static class TestHttp
{
    public sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(respond(request));
        }
    }

    public sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler) { BaseAddress = new Uri("http://api") };
    }

    public static HttpResponseMessage Token(string accessToken) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { accessToken, expiresAt = "2026-07-09T12:00:00Z", userId = "u" }),
    };

    // An ApiAuthClient whose /auth/refresh yields `refreshed` (null → a 401, i.e. refresh failed).
    public static ApiAuthClient AuthClient(string? refreshed) =>
        new(new Factory(new Stub(req =>
            req.RequestUri!.AbsolutePath == "/auth/refresh" && refreshed is null
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Token(refreshed ?? "unused"))));
}
