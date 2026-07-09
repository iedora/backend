using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Iedora.Api.IntegrationTests;

// Capture the measurements an in-process Meter emits on one instrument while `action` runs. The
// WebApplicationFactory runs in-proc, so a MeterListener here sees the app's Meters directly. Shared
// by every metric test instead of each re-declaring the listener plumbing.
internal static class MetricsCapture
{
    /// <summary>Counter (long) measurements: each value + its tags (as a string map).</summary>
    public static async Task<List<(long Value, IReadOnlyDictionary<string, string?> Tags)>> Counter(
        string meterName, string instrument, Func<Task> action)
    {
        var got = new ConcurrentBag<(long, IReadOnlyDictionary<string, string?>)>();
        using var listener = Listen(meterName, instrument);
        listener.SetMeasurementEventCallback<long>((_, m, tags, _) =>
        {
            var d = new Dictionary<string, string?>();
            foreach (var t in tags) d[t.Key] = t.Value as string;
            got.Add((m, d));
        });
        listener.Start();
        await action();
        listener.Dispose(); // flush
        return [.. got];
    }

    /// <summary>Histogram (int) measurements: just the recorded values.</summary>
    public static async Task<List<int>> Histogram(string meterName, string instrument, Func<Task> action)
    {
        var got = new ConcurrentBag<int>();
        using var listener = Listen(meterName, instrument);
        listener.SetMeasurementEventCallback<int>((_, m, _, _) => got.Add(m));
        listener.Start();
        await action();
        listener.Dispose();
        return [.. got];
    }

    private static MeterListener Listen(string meterName, string instrument) => new()
    {
        InstrumentPublished = (i, l) => { if (i.Meter.Name == meterName && i.Name == instrument) l.EnableMeasurementEvents(i); },
    };
}
