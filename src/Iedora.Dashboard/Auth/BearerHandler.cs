using System.Net.Http.Headers;

namespace Iedora.Dashboard;

/// <summary>Attaches the current admin's bearer token (from the request-scoped <see cref="AccessToken"/>)
/// to outgoing API calls, so the generated client hits the admin-only <c>/api/staff</c> surface
/// authenticated.</summary>
public sealed class BearerHandler(AccessToken token) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (token.Value is { Length: > 0 } value)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", value);
        return base.SendAsync(request, ct);
    }
}
