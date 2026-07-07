namespace Iedora.Menus;

// Public-view analytics (ported from the Bun menu service). Two dedup-gated counter pairs +
// raw session durations. All day/hour bucketing is UTC strings. Writes go through atomic
// dedup+increment SQL (see ViewTracking); the dashboard reads these aggregates.

/// <summary>Dedups one menu view per visitor/restaurant/hour — the marker that gates a daily count.</summary>
public sealed class ViewSeen
{
    public string VisitorId { get; set; } = "";
    public Guid RestaurantId { get; set; }
    public string HourBucket { get; set; } = ""; // UTC 'YYYY-MM-DD-HH'
    public DateTimeOffset SeenAt { get; set; }
}

/// <summary>Per-day, per-language menu-view counter.</summary>
public sealed class DailyView
{
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public string Day { get; set; } = ""; // UTC 'YYYY-MM-DD'
    public string Language { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Dedups one item view per visitor/item/day.</summary>
public sealed class ItemViewSeen
{
    public string VisitorId { get; set; } = "";
    public Guid ItemId { get; set; }
    public string Day { get; set; } = "";
    public DateTimeOffset SeenAt { get; set; }
}

/// <summary>Per-day item-view counter — powers "top dishes".</summary>
public sealed class ItemView
{
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ItemId { get; set; }
    public string Day { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>One guest session's dwell time (clamped 1..3600s) — raw rows; the average is read-time.</summary>
public sealed class MenuSession
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public string Day { get; set; } = "";
    public int DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
