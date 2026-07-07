using Iedora.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.UnitTests;

[TestClass]
public sealed class RefreshTokensTests
{
    [TestMethod]
    public void New_returns_a_token_whose_hash_matches_Hash()
    {
        var (token, hash) = RefreshTokens.New();
        Assert.IsTrue(hash.SequenceEqual(RefreshTokens.Hash(token))); // the stored digest verifies the raw token
    }

    [TestMethod]
    public void New_produces_unique_tokens()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => RefreshTokens.New().token).ToHashSet();
        Assert.HasCount(100, tokens);
    }

    [TestMethod]
    public void Hash_is_deterministic_and_32_bytes_sha256()
    {
        Assert.IsTrue(RefreshTokens.Hash("abc").SequenceEqual(RefreshTokens.Hash("abc")));
        Assert.IsFalse(RefreshTokens.Hash("abc").SequenceEqual(RefreshTokens.Hash("abd")));
        Assert.HasCount(32, RefreshTokens.Hash("abc"));
    }
}
