using Iedora.Api.Security;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Iedora.Api.Features.Jwks;

// GET /auth/.well-known/jwks.json — the ES256 public key so other services verify tokens
// offline. Public, cacheable. Public params only (never the private key).
public static class JwksEndpoint
{
    public static void MapJwks(this RouteGroupBuilder group) =>
        group.MapGet("/.well-known/jwks.json", Ok<JwksDocument> (JwtTokenService jwt, HttpResponse response) =>
        {
            response.Headers.CacheControl = "public, max-age=300";
            return TypedResults.Ok(jwt.Jwks());
        })
        .AllowAnonymous()
        .WithName("Jwks")
        .WithSummary("EC public keys (JWKS) for verifying access tokens offline.");
}
