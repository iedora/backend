using System.Diagnostics.Metrics;

namespace Iedora.Menus;

/// <summary>
/// The Menu module's OpenTelemetry instruments — defined once (static) and used directly, no wrapper
/// (per the "don't wrap OpenTelemetry" guidance). The <c>Iedora.Menus</c> Meter is exported by
/// ServiceDefaults' <c>AddMeter("Iedora.*")</c>. Tag <b>values</b> are strictly low-cardinality
/// (language, kind) — never slugs / ids / tenant ids, which would explode metric cardinality.
/// Per-restaurant analytics live in the DB (daily_view, …); these are aggregate, real-time signals.
/// <para>Every instrument name and tag key is a <c>const</c> here — the single source of truth shared
/// by the emit sites and the tests (DRY: rename once, no silent break).</para>
/// </summary>
public static class MenuMetrics
{
    public const string MeterName = "Iedora.Menus";

    /// <summary>Instrument names.</summary>
    public static class Instruments
    {
        public const string Views = "menu.views";
        public const string ItemViews = "menu.item_views";
        public const string Dwell = "menu.session.dwell";
        public const string Errors = "iedora.errors";
    }

    /// <summary>Tag keys.</summary>
    public static class Tags
    {
        public const string Language = "language";
        public const string Kind = "kind";
    }

    private static readonly Meter Meter = new(MeterName);

    // --- Business insight: public menu traffic + guest engagement ---

    /// <summary>Public menu view beacons served (raw traffic), tagged by resolved language.</summary>
    public static readonly Counter<long> Views =
        Meter.CreateCounter<long>(Instruments.Views, unit: "{view}", description: "Public menu view beacons served.");

    /// <summary>Dish views reported by session beacons (guest engagement depth).</summary>
    public static readonly Counter<long> ItemViews =
        Meter.CreateCounter<long>(Instruments.ItemViews, unit: "{view}", description: "Dish views reported by guests.");

    /// <summary>Guest session dwell time (seconds, clamped 1..3600) — how long guests read the menu.</summary>
    public static readonly Histogram<int> Dwell =
        Meter.CreateHistogram<int>(Instruments.Dwell, unit: "s", description: "Guest session dwell time.");

    // --- Error rate for the module (bounded tag: kind) ---

    /// <summary>Handled/recorded errors in the Menu module, tagged by a low-cardinality <c>kind</c>.</summary>
    public static readonly Counter<long> Errors =
        Meter.CreateCounter<long>(Instruments.Errors, description: "Handled errors recorded in the Menu module.");
}
