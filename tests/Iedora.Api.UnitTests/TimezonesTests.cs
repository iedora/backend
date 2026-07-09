using Iedora.Menus;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.UnitTests;

[TestClass]
public sealed class TimezonesTests
{
    // 23:30 UTC on 2026-07-09 — a moment that lands on different calendar days across zones.
    private static readonly DateTimeOffset LateUtc = new(2026, 7, 9, 23, 30, 0, TimeSpan.Zero);

    [TestMethod]
    public void LocalDay_uses_the_zones_calendar_day_not_utc()
    {
        Assert.AreEqual(new DateOnly(2026, 7, 9), Timezones.LocalDay(LateUtc, "UTC"));
        Assert.AreEqual(new DateOnly(2026, 7, 10), Timezones.LocalDay(LateUtc, "Europe/Lisbon"));   // +1 (summer) → next day
        Assert.AreEqual(new DateOnly(2026, 7, 9), Timezones.LocalDay(LateUtc, "Pacific/Honolulu")); // -10 → still the 9th (13:30)
    }

    [TestMethod]
    public void LocalDay_falls_back_to_utc_for_a_bad_or_missing_zone()
    {
        Assert.AreEqual(new DateOnly(2026, 7, 9), Timezones.LocalDay(LateUtc, "Not/AZone"));
        Assert.AreEqual(new DateOnly(2026, 7, 9), Timezones.LocalDay(LateUtc, null));
        Assert.AreEqual(new DateOnly(2026, 7, 9), Timezones.LocalDay(LateUtc, ""));
    }

    [TestMethod]
    public void LocalMonthStart_uses_the_zones_month()
    {
        // 2026-07-01 00:30 UTC is still June 30 in Honolulu (-10) → its month starts June 1.
        var justAfterUtcMonthStart = new DateTimeOffset(2026, 7, 1, 0, 30, 0, TimeSpan.Zero);
        Assert.AreEqual(new DateOnly(2026, 7, 1), Timezones.LocalMonthStart(justAfterUtcMonthStart, "UTC"));
        Assert.AreEqual(new DateOnly(2026, 6, 1), Timezones.LocalMonthStart(justAfterUtcMonthStart, "Pacific/Honolulu"));
    }

    [TestMethod]
    public void IsValid_distinguishes_real_zones()
    {
        Assert.IsTrue(Timezones.IsValid("Europe/Lisbon"));
        Assert.IsTrue(Timezones.IsValid("UTC"));
        Assert.IsFalse(Timezones.IsValid("Middle/Earth"));
    }
}
