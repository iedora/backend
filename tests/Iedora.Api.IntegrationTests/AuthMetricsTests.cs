using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// The auth business metrics on the "iedora-api" Meter: signups (registrations) and token issuance.
// Captured in-process with a MeterListener (the WebApplicationFactory runs in-proc).
[TestClass]
public sealed class AuthMetricsTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static async Task<List<Dictionary<string, string?>>> Capture(string instrument, Func<Task> action)
    {
        var got = new ConcurrentBag<Dictionary<string, string?>>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) => { if (i.Meter.Name == "iedora-api" && i.Name == instrument) l.EnableMeasurementEvents(i); },
        };
        listener.SetMeasurementEventCallback<long>((i, m, tags, _) =>
        {
            var d = new Dictionary<string, string?>();
            foreach (var t in tags) d[t.Key] = t.Value as string;
            got.Add(d);
        });
        listener.Start();
        await action();
        listener.Dispose(); // flush
        return [.. got];
    }

    [TestMethod]
    public async Task A_completed_registration_emits_a_created_signup_metric()
    {
        var tags = await Capture("iedora.auth.registrations", () => RegisterAccount("signup@m.pt", Pw));
        Assert.IsTrue(tags.Any(t => t.GetValueOrDefault("result") == "created"),
            "expected an iedora.auth.registrations measurement tagged result=created");
    }

    [TestMethod]
    public async Task A_duplicate_email_emits_a_rejected_signup_metric()
    {
        await RegisterAccount("dupe@m.pt", Pw); // first one succeeds
        var tags = await Capture("iedora.auth.registrations", async () => await Register("dupe@m.pt", Pw, "User"));
        Assert.IsTrue(tags.Any(t => t.GetValueOrDefault("result") == "rejected"),
            "expected an iedora.auth.registrations measurement tagged result=rejected");
    }

    [TestMethod]
    public async Task A_service_token_emits_a_tokens_issued_metric()
    {
        var tags = await Capture("iedora.auth.tokens_issued", async () => await ServiceToken());
        Assert.IsTrue(tags.Any(t => t.GetValueOrDefault("grant") == "client_credentials" && t.GetValueOrDefault("result") == "issued"),
            "expected an iedora.auth.tokens_issued measurement tagged grant=client_credentials, result=issued");
    }
}
