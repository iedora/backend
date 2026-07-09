using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Iedora.Api.UnitTests;

// Guards the ServiceDefaults OpenTelemetry wiring: the pipeline registers app signals by wildcard
// (AddMeter("Iedora.*")/("Framework.*"), AddSource(...)), so a Meter or ActivitySource whose name
// doesn't match — or that isn't registered at all — is silently dropped from export. (That is exactly
// how the Worker once lost its metrics.) This drives the REAL ConfigureOpenTelemetry pipeline with an
// in-memory exporter and asserts both namespaces, for both metrics and traces, actually export.
[TestClass]
public sealed class OpenTelemetryExportTests
{
    [TestMethod]
    public async Task ServiceDefaults_exports_app_meters_and_activity_sources_by_wildcard()
    {
        var metrics = new List<Metric>();
        var activities = new List<Activity>();

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureOpenTelemetry(); // the real ServiceDefaults pipeline (wildcards, no exporter yet)
        builder.Services.AddOpenTelemetry()
            .WithMetrics(m => m.AddInMemoryExporter(metrics))
            .WithTracing(t => t.AddInMemoryExporter(activities));

        using var host = builder.Build();
        await host.StartAsync(); // instantiates the providers + their listeners

        // Emit from names matching the app's conventions — a domain Meter/Source (Iedora.*) and a
        // shared framework one (Framework.*). Distinct instrument names so presence pins the meter.
        using (var domainMeter = new Meter("Iedora.Menus"))
        using (var infraMeter = new Meter("Framework.Maintenance"))
        {
            domainMeter.CreateCounter<long>("test.domain.metric").Add(1);
            infraMeter.CreateCounter<long>("test.infra.metric").Add(1);

            using (var domainSource = new ActivitySource("Iedora.Identity"))
            using (var infraSource = new ActivitySource("Framework.Commands"))
            {
                using (domainSource.StartActivity("domain.span")) { }
                using (infraSource.StartActivity("infra.span")) { }
            }

            host.Services.GetRequiredService<MeterProvider>().ForceFlush();
            host.Services.GetRequiredService<TracerProvider>().ForceFlush();
        }

        await host.StopAsync();

        var metricNames = metrics.Select(m => m.Name).ToHashSet();
        Assert.Contains("test.domain.metric", metricNames, "Iedora.* meter did not export");
        Assert.Contains("test.infra.metric", metricNames, "Framework.* meter did not export");

        var sourceNames = activities.Select(a => a.Source.Name).ToHashSet();
        Assert.Contains("Iedora.Identity", sourceNames, "Iedora.* activity source did not export");
        Assert.Contains("Framework.Commands", sourceNames, "Framework.* activity source did not export");
    }
}
