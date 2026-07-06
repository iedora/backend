using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Iedora.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Iedora.Api.Security;

/// <summary>
/// Issues ES256 (ECDSA P-256) access-token JWTs and serves the JWKS. ES256 is
/// framework-native (no third-party crypto, unlike Ed25519) and validates directly with
/// the built-in <c>AddJwtBearer</c> handler + <see cref="ValidationParameters"/>, so other
/// services can verify tokens offline via the JWKS. Reads a stable PEM key from config
/// (API_JWT_EC_PRIVATE_KEY); if absent, generates one at startup (fine for dev — the
/// JWKS + validator use the same in-memory key).
/// </summary>
public sealed class JwtTokenService
{
    private readonly ECDsa _ecdsa;
    private readonly ECDsaSecurityKey _key;
    private readonly TimeProvider _clock;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _ttl;

    public string Kid { get; }

    public JwtTokenService(IConfiguration cfg, TimeProvider clock)
    {
        _clock = clock;
        _issuer = cfg["API_JWT_ISSUER"] ?? "https://api.iedora.com";
        _audience = cfg["API_JWT_AUDIENCE"] ?? "iedora-api";
        _ttl = TimeSpan.FromMinutes(int.Parse(cfg["API_ACCESS_TTL_MIN"] ?? "15"));
        Kid = cfg["API_JWT_KEY_ID"] ?? "k1";

        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = cfg["API_JWT_EC_PRIVATE_KEY"];
        if (!string.IsNullOrWhiteSpace(pem)) _ecdsa.ImportFromPem(pem);
        _key = new ECDsaSecurityKey(_ecdsa) { KeyId = Kid };
    }

    /// <summary>
    /// Mints an access token bound to a session. Claims match the wire contract the frontend
    /// decodes: <c>sub</c>, <c>email</c>, <c>roles</c>, <c>typ=access</c>, <c>sid</c> (session
    /// family id), and optionally <c>tid</c> (tenant) / <c>mcp</c> (must-change-password).
    /// Time comes from <see cref="TimeProvider"/> so token expiry is deterministic in tests.
    /// </summary>
    public (string token, DateTimeOffset expiresAt) Issue(
        AppUser user, IEnumerable<string> roles, Guid sessionId, Guid? tenantId, bool mustChangePassword)
    {
        var now = _clock.GetUtcNow();
        var expiresAt = now.Add(_ttl);
        var claims = new Dictionary<string, object>
        {
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email ?? "",
            ["roles"] = roles.ToArray(),
            ["typ"] = "access",
            ["sid"] = sessionId.ToString(),
        };
        if (tenantId is { } tid) claims["tid"] = tid.ToString();
        if (mustChangePassword) claims["mcp"] = true;

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.EcdsaSha256),
        });
        return (token, expiresAt);
    }

    /// <summary>TokenValidationParameters for the built-in JwtBearer handler (same key).</summary>
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = _issuer,
        ValidAudience = _audience,
        IssuerSigningKey = _key,
        ValidateIssuerSigningKey = true,
        ValidateLifetime = true,
        NameClaimType = "sub",
        RoleClaimType = "roles",
    };

    /// <summary>Public-only JWK Set. Exports ONLY the public EC params — never the private d.</summary>
    public JwksDocument Jwks()
    {
        var p = _ecdsa.ExportParameters(includePrivateParameters: false);
        return new JwksDocument([
            new Jwk("EC", "P-256", "sig", "ES256", Kid,
                Base64UrlEncoder.Encode(p.Q.X), Base64UrlEncoder.Encode(p.Q.Y)),
        ]);
    }
}

public sealed record Jwk(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("crv")] string Crv,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("x")] string X,
    [property: JsonPropertyName("y")] string Y);

public sealed record JwksDocument(
    [property: JsonPropertyName("keys")] IReadOnlyList<Jwk> Keys);
