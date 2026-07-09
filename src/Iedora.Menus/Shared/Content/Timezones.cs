namespace Iedora.Menus;

/// <summary>
/// IANA-timezone helpers for analytics bucketing. Analytics count by the RESTAURANT's local calendar
/// day (not server UTC), so an owner's "today" flips at their local midnight — not at 00:00 UTC.
/// .NET resolves IANA ids (e.g. "Europe/Lisbon") cross-platform via ICU. An unknown/blank id falls
/// back to UTC so a bad value can never throw on the (fire-and-forget) write path.
/// </summary>
public static class Timezones
{
    public static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>The calendar day <paramref name="instant"/> falls on in the given zone (UTC if unknown).</summary>
    public static DateOnly LocalDay(DateTimeOffset instant, string? tzId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Resolve(tzId)).DateTime);

    /// <summary>First day of the calendar month <paramref name="instant"/> falls on, in the given zone.</summary>
    public static DateOnly LocalMonthStart(DateTimeOffset instant, string? tzId)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Resolve(tzId));
        return new DateOnly(local.Year, local.Month, 1);
    }

    /// <summary>Whether <paramref name="id"/> is a real IANA/system timezone (for input validation).</summary>
    public static bool IsValid(string id)
    {
        try { TimeZoneInfo.FindSystemTimeZoneById(id); return true; }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }
}
