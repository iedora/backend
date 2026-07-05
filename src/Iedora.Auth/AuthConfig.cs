namespace Iedora.Auth;

/// <summary>
/// Env-driven configuration. Var names match the deployed Bun service exactly, so the
/// same Kamal env/secrets map over unchanged. Only the fields the ported slices need are
/// here; it grows as more features land (DB URLs, cookies, reset TTLs, SMTP, ...).
/// </summary>
public sealed class AuthConfig
{
    public required int Port { get; init; }
    public required string JwtSeed { get; init; }          // base64 32-byte Ed25519 seed
    public required string JwtKeyId { get; init; }
    public required string JwtIssuer { get; init; }
    public required string JwtAudience { get; init; }      // access-token audience (iedora-api)
    public required string ServiceAudience { get; init; }  // service-token audience (iedora-internal)
    public required TimeSpan AccessTtl { get; init; }
    public required TimeSpan ServiceTokenTtl { get; init; }
    public required string ServiceClients { get; init; }   // "id:secret,id2:secret2"

    public static AuthConfig Load(IConfiguration c) => new()
    {
        Port = int.Parse(c["AUTH_PORT"] ?? "8080"),
        JwtSeed = Require(c, "API_JWT_PRIVATE_KEY"),
        JwtKeyId = c["API_JWT_KEY_ID"] ?? "k1",
        JwtIssuer = Require(c, "API_JWT_ISSUER"),
        JwtAudience = c["API_JWT_AUDIENCE"] ?? "iedora-api",
        ServiceAudience = c["SERVICE_AUDIENCE"] ?? "iedora-internal",
        AccessTtl = ParseDuration(c["API_ACCESS_TTL"], TimeSpan.FromMinutes(15)),
        ServiceTokenTtl = ParseDuration(c["SERVICE_TOKEN_TTL"], TimeSpan.FromMinutes(10)),
        ServiceClients = c["SERVICE_CLIENTS"] ?? "",
    };

    private static string Require(IConfiguration c, string key) =>
        c[key] ?? throw new InvalidOperationException($"missing required env var {key}");

    /// <summary>Parses a jose-style duration ("15m", "720h", "60s", "10m") into a TimeSpan.</summary>
    internal static TimeSpan ParseDuration(string? raw, TimeSpan fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var s = raw.Trim();
        var unit = s[^1];
        if (char.IsDigit(unit)) return TimeSpan.FromMilliseconds(double.Parse(s)); // bare number = ms
        if (!double.TryParse(s[..^1], out var n)) return fallback;
        return unit switch
        {
            's' => TimeSpan.FromSeconds(n),
            'm' => TimeSpan.FromMinutes(n),
            'h' => TimeSpan.FromHours(n),
            'd' => TimeSpan.FromDays(n),
            _ => fallback,
        };
    }
}
