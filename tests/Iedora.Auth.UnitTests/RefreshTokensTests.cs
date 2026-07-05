using Iedora.Auth.Security;
using Xunit;

namespace Iedora.Auth.UnitTests;

public sealed class RefreshTokensTests
{
    [Fact]
    public void New_returns_a_token_whose_hash_matches_Hash()
    {
        var (token, hash) = RefreshTokens.New();
        Assert.Equal(hash, RefreshTokens.Hash(token)); // the stored digest verifies the raw token
    }

    [Fact]
    public void New_produces_unique_tokens()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => RefreshTokens.New().token).ToHashSet();
        Assert.Equal(100, tokens.Count);
    }

    [Fact]
    public void Hash_is_deterministic_and_32_bytes_sha256()
    {
        Assert.Equal(RefreshTokens.Hash("abc"), RefreshTokens.Hash("abc"));
        Assert.NotEqual(RefreshTokens.Hash("abc"), RefreshTokens.Hash("abd"));
        Assert.Equal(32, RefreshTokens.Hash("abc").Length);
    }
}
