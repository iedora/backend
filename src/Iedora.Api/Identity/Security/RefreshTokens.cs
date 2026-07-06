using System.Security.Cryptography;

namespace Iedora.Api.Security;

/// <summary>
/// Opaque refresh tokens: 32 cryptographically-random bytes, base64url-encoded (43 chars),
/// handed to the client in the refresh cookie. Only the SHA-256 digest is ever persisted, so
/// a leaked database row can't be replayed. Pure + deterministic hashing → unit-testable.
/// </summary>
public static class RefreshTokens
{
    private const int TokenBytes = 32;

    /// <summary>Mint a new token and its digest. The token goes to the client; the hash to the DB.</summary>
    public static (string token, byte[] hash) New()
    {
        var raw = RandomNumberGenerator.GetBytes(TokenBytes);
        var token = Base64UrlEncode(raw);
        return (token, Hash(token));
    }

    /// <summary>SHA-256 digest of a token, as stored in <c>sessions.TokenHash</c>.</summary>
    public static byte[] Hash(string token) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
