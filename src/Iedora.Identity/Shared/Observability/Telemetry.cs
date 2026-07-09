using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Iedora.Identity;

/// <summary>
/// The module's own <see cref="ActivitySource"/> (for manual business spans) and
/// <see cref="Meter"/> (for custom metrics). Both are BCL types — no OTel package
/// needed to CREATE telemetry; the SDK listens because the name matches ServiceDefaults'
/// <c>AddSource("Iedora.*")</c> / <c>AddMeter("Iedora.*")</c> — the same convention as
/// <c>MenuMetrics</c>, and (unlike an explicit per-module registration) it flows from the
/// Worker too, where the outbox handlers emit.
/// <para>Every instrument name, tag key and tag value is a <c>const</c> here — the single source
/// of truth shared by the emit sites and the tests (DRY: a rename is one edit, not a silent break).</para>
/// </summary>
public static class Telemetry
{
    public const string MeterName = "Iedora.Identity";
    public const string ActivitySourceName = MeterName;

    /// <summary>Instrument names.</summary>
    public static class Instruments
    {
        public const string TokensIssued = "iedora.auth.tokens_issued";
        public const string Registrations = "iedora.auth.registrations";
        public const string SessionsRotated = "iedora.auth.sessions_rotated";
        public const string ReuseDetected = "iedora.auth.reuse_detected";
    }

    /// <summary>Tag keys.</summary>
    public static class Tags
    {
        public const string Grant = "grant";
        public const string Result = "result";
    }

    /// <summary>Values for the <see cref="Tags.Grant"/> tag.</summary>
    public static class Grant
    {
        public const string Password = "password";
        public const string ClientCredentials = "client_credentials";
    }

    /// <summary>Values for the <see cref="Tags.Result"/> tag.</summary>
    public static class Result
    {
        public const string Issued = "issued";
        public const string Denied = "denied";
        public const string Created = "created";
        public const string Rejected = "rejected";
    }

    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);

    private static readonly Meter Meter = new(MeterName, ServiceVersion);

    /// <summary>Tokens issued, by grant type + result — a business metric.</summary>
    public static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>(
        Instruments.TokensIssued, unit: "{token}", description: "Access/service tokens issued");

    /// <summary>Account registrations (signups), by result (created / rejected) — the growth funnel.</summary>
    public static readonly Counter<long> Registrations = Meter.CreateCounter<long>(
        Instruments.Registrations, unit: "{account}", description: "Account registrations, by result");

    /// <summary>Refresh-token rotations (successful session renewals).</summary>
    public static readonly Counter<long> SessionsRotated = Meter.CreateCounter<long>(
        Instruments.SessionsRotated, unit: "{session}", description: "Refresh-token rotations");

    /// <summary>Reuse detections — a spent/rotated refresh token was presented, burning the
    /// whole family. A spike here is a token-theft signal worth alarming on.</summary>
    public static readonly Counter<long> ReuseDetected = Meter.CreateCounter<long>(
        Instruments.ReuseDetected, unit: "{event}", description: "Refresh-token reuse detections (family burned)");
}
