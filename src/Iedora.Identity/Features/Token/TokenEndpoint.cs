using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Iedora.Identity;

/// <summary>Configured internal service clients (client-credentials grant). Bound from the
/// <c>ServiceToken</c> config section: <c>ServiceToken:Clients:{clientId} = {secret}</c>.</summary>
public sealed class ServiceTokenOptions
{
    public Dictionary<string, string> Clients { get; init; } = new();
}

/// <summary>The client-credentials grant response — a Bearer service token and its expiry.</summary>
public sealed record ServiceTokenResponse(string AccessToken, DateTimeOffset ExpiresAt, string TokenType);

// POST /auth/token — OAuth2 client-credentials grant. HTTP Basic clientId:secret (matched in constant
// time against the configured service clients) → an internal service token (typ=service). Used by
// backend services (e.g. the menu BFF) to call service-gated endpoints. Any failure → a flat 401.
public static class TokenEndpoint
{
    public static void MapToken(this RouteGroupBuilder group) =>
        group.MapPost("/token",
            Results<Ok<ServiceTokenResponse>, UnauthorizedHttpResult> (
                HttpContext http, JwtTokenService jwt, IOptions<ServiceTokenOptions> options) =>
        {
            if (!TryReadBasic(http.Request.Headers.Authorization, out var clientId, out var secret))
                return TypedResults.Unauthorized();

            if (!options.Value.Clients.TryGetValue(clientId, out var expected) || !SecretsMatch(secret, expected))
            {
                Telemetry.TokensIssued.Add(1, new("grant", "client_credentials"), new("result", "denied"));
                return TypedResults.Unauthorized();
            }

            var (token, expiresAt) = jwt.IssueService(clientId);
            Telemetry.TokensIssued.Add(1, new("grant", "client_credentials"), new("result", "issued"));
            return TypedResults.Ok(new ServiceTokenResponse(token, expiresAt, "Bearer"));
        })
        .AllowAnonymous()
        .WithName("Token")
        .WithSummary("Client-credentials grant: HTTP Basic clientId:secret → an internal service token.")
        .Produces<ServiceTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized);

    private static bool TryReadBasic(string? authorization, out string clientId, out string secret)
    {
        clientId = secret = "";
        if (!AuthenticationHeaderValue.TryParse(authorization, out var header) ||
            !"Basic".Equals(header.Scheme, StringComparison.OrdinalIgnoreCase) ||
            header.Parameter is null)
            return false;

        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)); }
        catch (FormatException) { return false; }

        var sep = decoded.IndexOf(':'); // secrets may contain ':', so split on the first only
        if (sep <= 0) return false;
        clientId = decoded[..sep];
        secret = decoded[(sep + 1)..];
        return secret.Length > 0;
    }

    private static bool SecretsMatch(string provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
