namespace Iedora.Menus;

// Public-view analytics (ported from the Bun menu service). Two dedup-gated counter pairs +
// raw session durations. Buckets are native Postgres types — `date` for the day, a truncated
// `timestamptz` for the hour — not text, so keys are half the width and range reads over a
// date span are index-native. Writes go through atomic dedup+increment SQL (see ViewTracking);
// the dashboard reads these aggregates.

/// <summary>Dedups one menu view per visitor/restaurant/hour — the marker that gates a daily count.</summary>
public sealed class ViewSeen
{
    public Guid VisitorId { get; set; }
    public Guid RestaurantId { get; set; }
    public DateTimeOffset HourStart { get; set; } // UTC, truncated to the hour
}

/// <summary>Per-day, per-language menu-view counter.</summary>
public sealed class DailyView
{
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public DateOnly Day { get; set; }
    public string Language { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>Dedups one item view per visitor/item/day.</summary>
public sealed class ItemViewSeen
{
    public Guid VisitorId { get; set; }
    public Guid ItemId { get; set; }
    public DateOnly Day { get; set; }
}

/// <summary>Per-day item-view counter — powers "top dishes".</summary>
public sealed class ItemView
{
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ItemId { get; set; }
    public DateOnly Day { get; set; }
    public int Count { get; set; }
}

/// <summary>One guest session's dwell time (clamped 1..3600s — fits smallint) — raw rows; the
/// average is computed read-time.</summary>
public sealed class MenuSession
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public Guid TenantId { get; set; }
    public DateOnly Day { get; set; }
    public short DurationSeconds { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
