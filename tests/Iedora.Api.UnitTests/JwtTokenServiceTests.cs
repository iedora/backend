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

    private static JwtTokenService Build(TimeProvider clock)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["API_JWT_ISSUER"] = "https://api.iedora.com",
            ["API_JWT_AUDIENCE"] = "iedora-api",
            ["API_ACCESS_TTL_MIN"] = "15",
        }).Build();
        return new JwtTokenService(cfg, clock);
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
}
