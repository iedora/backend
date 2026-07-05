using Microsoft.Extensions.Options;

namespace Iedora.Auth.Sessions;

/// <summary>
/// Reads/writes the refresh cookie: HttpOnly, SameSite=Lax, scoped to <c>/auth</c> so it's
/// only sent to the auth service. The cookie value is the raw opaque refresh token.
/// </summary>
public sealed class RefreshCookie(IOptions<SessionSettings> options)
{
    private const string Path = "/auth";
    private readonly SessionSettings _opt = options.Value;

    public string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(_opt.RefreshCookieName, out var v) ? v : null;

    public void Set(HttpResponse response, string token, DateTimeOffset expiresAt) =>
        response.Cookies.Append(_opt.RefreshCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = _opt.CookieSecure,
            SameSite = SameSiteMode.Lax,
            Path = Path,
            Expires = expiresAt,
            Domain = string.IsNullOrEmpty(_opt.CookieDomain) ? null : _opt.CookieDomain,
        });

    public void Clear(HttpResponse response) =>
        response.Cookies.Delete(_opt.RefreshCookieName, new CookieOptions
        {
            Path = Path,
            Domain = string.IsNullOrEmpty(_opt.CookieDomain) ? null : _opt.CookieDomain,
        });
}
