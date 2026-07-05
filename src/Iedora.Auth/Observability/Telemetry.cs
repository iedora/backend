using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Iedora.Auth.Observability;

/// <summary>
/// The service's own <see cref="ActivitySource"/> (for manual business spans) and
/// <see cref="Meter"/> (for custom metrics). Both are BCL types — no OTel package
/// needed to CREATE telemetry; the SDK only listens once these names are registered
/// via <c>AddSource</c>/<c>AddMeter</c> (see <see cref="OpenTelemetryExtensions"/>).
/// Named "iedora-auth" so it matches the Bun service's <c>service.name</c> convention.
/// </summary>
public static class Telemetry
{
    public const string ServiceName = "iedora-auth";
    public const string ActivitySourceName = "iedora-auth";
    public const string MeterName = "iedora-auth";

    public static readonly string ServiceVersion =
        typeof(Telemetry).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, ServiceVersion);

    private static readonly Meter Meter = new(MeterName, ServiceVersion);

    /// <summary>Tokens issued, by grant type + result — a business metric.</summary>
    public static readonly Counter<long> TokensIssued = Meter.CreateCounter<long>(
        "iedora.auth.tokens_issued", unit: "{token}", description: "Access/service tokens issued");
}
