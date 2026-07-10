using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Dashboard.Tests;

[TestClass]
public sealed class JwtTests
{
    // Build a JWT-shaped string (header.payload.signature) with the given payload — only the payload
    // segment is read, and the signature is ignored (the API validates the real thing).
    private static string TokenWith(object payload)
    {
        static string Seg(object o) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(o))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Seg(new { alg = "ES256" })}.{Seg(payload)}.sig";
    }

    [TestMethod]
    public void Reads_sub_email_and_roles_onto_standard_claim_types()
    {
        var token = TokenWith(new { sub = "user-1", email = "a@b.pt", roles = new[] { "admin", "staff" } });

        var claims = Jwt.ReadClaims(token);

        Assert.AreEqual("user-1", claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.AreEqual("a@b.pt", claims.Single(c => c.Type == ClaimTypes.Name).Value);
        CollectionAssert.AreEquivalent(new[] { "admin", "staff" },
            claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList());
    }

    [TestMethod]
    public void Handles_a_token_with_no_roles()
    {
        var token = TokenWith(new { sub = "user-2", email = "c@d.pt" });

        var claims = Jwt.ReadClaims(token);

        Assert.IsFalse(claims.Any(c => c.Type == ClaimTypes.Role));
        Assert.AreEqual("user-2", claims.Single(c => c.Type == ClaimTypes.NameIdentifier).Value);
    }

    [TestMethod]
    public void A_malformed_token_yields_no_claims()
    {
        Assert.AreEqual(0, Jwt.ReadClaims("not-a-jwt").Count);
    }
}
