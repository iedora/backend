using System.Security.Claims;
using System.Text.Json;

namespace Iedora.Dashboard;

/// <summary>Read-only extraction of the API access token's claims (sub / email / roles) onto standard
/// claim types so <c>[Authorize]</c> and role checks work. No signature validation — the token is the
/// admin's own and the API validates it on every call; the dashboard only needs to read it.</summary>
public static class Jwt
{
    public static List<Claim> ReadClaims(string token)
    {
        var claims = new List<Claim>();
        var parts = token.Split('.');
        if (parts.Length < 2) return claims;

        using var doc = JsonDocument.Parse(Decode(parts[1]));
        var root = doc.RootElement;
        if (root.TryGetProperty("sub", out var sub) && sub.GetString() is { } id)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, id));
        if (root.TryGetProperty("email", out var email) && email.GetString() is { } addr)
            claims.Add(new Claim(ClaimTypes.Name, addr));
        if (root.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
            foreach (var role in roles.EnumerateArray())
                if (role.GetString() is { } r)
                    claims.Add(new Claim(ClaimTypes.Role, r));
        return claims;
    }

    // Base64url → bytes (JWT segments are base64url without padding).
    private static byte[] Decode(string segment)
    {
        var s = segment.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
