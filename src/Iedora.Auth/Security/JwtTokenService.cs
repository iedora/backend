using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Iedora.Auth.Data;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Iedora.Auth.Security;

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
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _ttl;

    public string Kid { get; }

    public JwtTokenService(IConfiguration cfg)
    {
        _issuer = cfg["API_JWT_ISSUER"] ?? "https://api.iedora.com";
        _audience = cfg["API_JWT_AUDIENCE"] ?? "iedora-api";
        _ttl = TimeSpan.FromMinutes(int.Parse(cfg["API_ACCESS_TTL_MIN"] ?? "15"));
        Kid = cfg["API_JWT_KEY_ID"] ?? "k1";

        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pem = cfg["API_JWT_EC_PRIVATE_KEY"];
        if (!string.IsNullOrWhiteSpace(pem)) _ecdsa.ImportFromPem(pem);
        _key = new ECDsaSecurityKey(_ecdsa) { KeyId = Kid };
    }

    /// <summary>Mints an access token for a signed-in user. Claims: sub, email, name, roles.</summary>
    public (string token, DateTimeOffset expiresAt) Issue(AppUser user, IEnumerable<string> roles)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.Add(_ttl);
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = user.Id.ToString(),
                ["email"] = user.Email ?? "",
                ["name"] = user.DisplayName ?? "",
                ["roles"] = roles.ToArray(),
            },
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
