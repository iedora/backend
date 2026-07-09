namespace Framework.Web.Tests;

[TestClass]
public sealed class PagingTests
{
    [TestMethod]
    public void Null_inputs_fall_back_to_the_default_page_and_first_offset()
    {
        var (take, skip) = Paging.Clamp(null, null);
        Assert.AreEqual(Paging.DefaultLimit, take);
        Assert.AreEqual(0, skip);
    }

    [TestMethod]
    public void An_oversized_limit_is_capped_at_the_maximum()
    {
        Assert.AreEqual(Paging.MaxLimit, Paging.Clamp(Paging.MaxLimit + 1_000).Take);
    }

    [TestMethod]
    public void Non_positive_limits_clamp_up_to_one_row()
    {
        Assert.AreEqual(1, Paging.Clamp(0).Take);
        Assert.AreEqual(1, Paging.Clamp(-5).Take);
    }

    [TestMethod]
    public void Offset_is_floored_at_zero_and_capped_at_the_maximum()
    {
        Assert.AreEqual(0, Paging.Clamp(10, -1).Skip);
        Assert.AreEqual(Paging.MaxOffset, Paging.Clamp(10, Paging.MaxOffset + 1).Skip);
    }

    [TestMethod]
    public void A_valid_request_passes_through_unchanged()
    {
        var (take, skip) = Paging.Clamp(25, 100);
        Assert.AreEqual(25, take);
        Assert.AreEqual(100, skip);
    }
}
