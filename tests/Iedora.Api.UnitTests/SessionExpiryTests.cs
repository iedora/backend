using Iedora.Identity;
using Iedora.Menus;
using Iedora.Tenancy;

namespace Iedora.Api.UnitTests;

[TestClass]
public sealed class SessionExpiryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    private static Session At(TimeSpan sliding, TimeSpan absolute, DateTimeOffset? revoked = null, Guid? replacedBy = null) =>
        new()
        {
            ExpiresAt = Now + sliding,
            AbsoluteExpiresAt = Now + absolute,
            RevokedAt = revoked,
            ReplacedBy = replacedBy,
        };

    [TestMethod]
    public void Live_when_not_revoked_and_before_both_expiries() =>
        Assert.IsTrue(At(sliding: TimeSpan.FromDays(1), absolute: TimeSpan.FromDays(30)).IsLive(Now));

    [TestMethod]
    public void Dead_when_past_sliding_expiry() =>
        Assert.IsFalse(At(sliding: TimeSpan.FromHours(-1), absolute: TimeSpan.FromDays(30)).IsLive(Now));

    [TestMethod]
    public void Dead_when_past_absolute_cap_even_if_sliding_is_fresh() =>
        Assert.IsFalse(At(sliding: TimeSpan.FromDays(1), absolute: TimeSpan.FromHours(-1)).IsLive(Now));

    [TestMethod]
    public void Dead_when_revoked() =>
        Assert.IsFalse(At(sliding: TimeSpan.FromDays(1), absolute: TimeSpan.FromDays(30), revoked: Now).IsLive(Now));

    [TestMethod]
    public void Rotated_when_replaced_or_revoked()
    {
        Assert.IsTrue(At(TimeSpan.FromDays(1), TimeSpan.FromDays(30), replacedBy: Guid.NewGuid()).IsRotated);
        Assert.IsTrue(At(TimeSpan.FromDays(1), TimeSpan.FromDays(30), revoked: Now).IsRotated);
        Assert.IsFalse(At(TimeSpan.FromDays(1), TimeSpan.FromDays(30)).IsRotated);
    }
}
