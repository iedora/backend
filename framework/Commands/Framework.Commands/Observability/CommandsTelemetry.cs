using System.Diagnostics.Metrics;

namespace Framework.Commands;

/// <summary>
/// Instruments for the async-write command pipeline. Generic (Framework) — an app exports them by
/// adding <c>AddMeter("Framework.*")</c> to its OpenTelemetry setup. Defined once, used directly (no
/// wrapper). Every instrument name and tag key is a <c>const</c> here — the single source of truth
/// shared by the emit site and the tests.
/// </summary>
public static class CommandsTelemetry
{
    public const string MeterName = "Framework.Commands";

    /// <summary>Instrument names.</summary>
    public static class Instruments
    {
        public const string Failed = "commands.failed";
    }

    /// <summary>Tag keys. Both values are bounded — the command <c>Type</c> and the error <c>Code</c>
    /// come from fixed catalogs, so cardinality stays low.</summary>
    public static class Tags
    {
        public const string Command = "command";
        public const string Code = "code";
    }

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Commands that ended in a terminal failure (an <c>ExecuteAsync</c> Error), tagged by
    /// command type + error code. One increment per failed command — a terminal state is reached once,
    /// so idempotent redelivery doesn't double-count.</summary>
    public static readonly Counter<long> Failed = Meter.CreateCounter<long>(
        Instruments.Failed, unit: "{command}", description: "Async-write commands that ended in a terminal failure.");
}
