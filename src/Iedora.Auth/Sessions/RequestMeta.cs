namespace Iedora.Auth.Sessions;

/// <summary>Device metadata stamped on sessions for the account/admin device views.</summary>
public readonly record struct RequestMeta(string? UserAgent, string? Ip)
{
    public static RequestMeta From(HttpContext http)
    {
        var ua = http.Request.Headers.UserAgent.ToString();
        // First segment of X-Forwarded-For (client IP), falling back to the socket peer.
        var xff = http.Request.Headers["X-Forwarded-For"].ToString();
        var ip = string.IsNullOrEmpty(xff)
            ? http.Connection.RemoteIpAddress?.ToString()
            : xff.Split(',')[0].Trim();
        return new RequestMeta(
            string.IsNullOrEmpty(ua) ? null : ua,
            string.IsNullOrEmpty(ip) ? null : ip);
    }
}
