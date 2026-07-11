using Iedora.Identity;
using Iedora.Menus;
using Iedora.Tenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Iedora.Api.UnitTests;

[TestClass]
public sealed class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);
    private const string Issuer = "https://api.iedora.com";
    private const string Audience = "iedora-api";

    private static JwtTokenService Build(TimeProvider clock, string? ecPrivateKey = null)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_JWT_ISSUER"] = Issuer,
            ["API_JWT_AUDIENCE"] = Audience,
            ["API_ACCESS_TTL_MIN"] = "15",
            ["API_JWT_EC_PRIVATE_KEY"] = ecPrivateKey,
        }).Build();
        return new JwtTokenService(cfg, clock);
    }

    // A P-256 private key as PEM, and as the single-line base64 of that PEM (how Kamal/Docker
    // secrets carry it, since --env-file can't hold the PEM's newlines).
    private static (string pem, string base64) NewKey()
    {
        using var ec = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var pem = ec.ExportPkcs8PrivateKeyPem();
        return (pem, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(pem)));
    }

    // Validate signature + claims without wall-clock lifetime (issuance uses a fixed fake time;
    // token expiry itself is asserted via expiresAt and covered end-to-end by integration tests).
    private static Task<TokenValidationResult> ValidateClaims(string token, JwtTokenService jwt)
    {
        var vp = jwt.ValidationParameters();
        vp.ValidateLifetime = false;
        return new JsonWebTokenHandler().ValidateTokenAsync(token, vp);
    }

    [TestMethod]
    public async Task Issued_token_validates_and_carries_session_claims()
    {
        var jwt = Build(new FakeTimeProvider(Now));
        var user = new AppUser { Id = Guid.NewGuid(), Email = "chef@bistro.pt" };
        var sid = Guid.NewGuid();
        var tid = Guid.NewGuid();

        var (token, expiresAt) = jwt.Issue(user, ["admin"], sid, tid, mustChangePassword: true);

        // Expiry is driven by the (fake) clock, not wall time.
        Assert.AreEqual(Now.AddMinutes(15), expiresAt);

        var result = await ValidateClaims(token, jwt);
        Assert.IsTrue(result.IsValid);

        var jwtToken = (JsonWebToken)result.SecurityToken;
        Assert.AreEqual(user.Id.ToString(), jwtToken.GetPayloadValue<string>("sub"));
        Assert.AreEqual("chef@bistro.pt", jwtToken.GetPayloadValue<string>("email"));
        Assert.AreEqual(sid.ToString(), jwtToken.GetPayloadValue<string>("sid"));
        Assert.AreEqual(tid.ToString(), jwtToken.GetPayloadValue<string>("tid"));
        Assert.AreEqual("access", jwtToken.GetPayloadValue<string>("typ"));
        Assert.IsTrue(jwtToken.GetPayloadValue<bool>("mcp"));
        Assert.Contains("admin", jwtToken.GetPayloadValue<string[]>("roles"));
    }

    [TestMethod]
    public async Task Optional_claims_are_omitted_when_absent()
    {
        var jwt = Build(new FakeTimeProvider(Now));
        var (token, _) = jwt.Issue(new AppUser { Id = Guid.NewGuid() }, [], Guid.NewGuid(),
            tenantId: null, mustChangePassword: false);

        var result = await ValidateClaims(token, jwt);
        var jwtToken = (JsonWebToken)result.SecurityToken;

        Assert.IsFalse(jwtToken.TryGetPayloadValue<string>("tid", out _)); // no tenant ⇒ no tid
        Assert.IsFalse(jwtToken.TryGetPayloadValue<bool>("mcp", out _));   // not forced ⇒ no mcp
    }

    // A configured key must be loaded (not regenerated per instance), so a token from one instance
    // verifies under another built from the SAME key — the property a deployment relies on across
    // API replicas. With an ephemeral key each instance would differ and validation would fail.
    [TestMethod]
    public async Task A_base64_encoded_key_is_loaded_and_stable_across_instances()
    {
        var (_, base64) = NewKey();
        var (token, _) = Build(new FakeTimeProvider(Now), base64)
            .Issue(new AppUser { Id = Guid.NewGuid() }, [], Guid.NewGuid(), null, false);

        var result = await ValidateClaims(token, Build(new FakeTimeProvider(Now), base64));
        Assert.IsTrue(result.IsValid);
    }

    [TestMethod]
    public async Task A_raw_pem_key_is_still_accepted()
    {
        var (pem, _) = NewKey();
        var (token, _) = Build(new FakeTimeProvider(Now), pem)
            .Issue(new AppUser { Id = Guid.NewGuid() }, [], Guid.NewGuid(), null, false);

        var result = await ValidateClaims(token, Build(new FakeTimeProvider(Now), pem));
        Assert.IsTrue(result.IsValid);
    }
}
