namespace Iedora.Menus;

/// <summary>A day and its count — a point on a daily chart (the owner dashboard's views breakdown,
/// the staff detail's trend).</summary>
public sealed record DailyPoint(DateOnly Day, int Count);

internal static class DailyPoints
{
    /// <summary>A dense, oldest-first series over <c>[today - daysBack, today]</c> — each day's count
    /// from <paramref name="counts"/>, zero when a day is missing. Shared by the dashboard breakdown
    /// and the staff trend.</summary>
    public static List<DailyPoint> ZeroFilled(DateOnly today, int daysBack, IReadOnlyDictionary<DateOnly, int> counts)
    {
        var points = new List<DailyPoint>(daysBack + 1);
        for (var d = -daysBack; d <= 0; d++)
        {
            var day = today.AddDays(d);
            points.Add(new DailyPoint(day, counts.GetValueOrDefault(day)));
        }
        return points;
    }
}
