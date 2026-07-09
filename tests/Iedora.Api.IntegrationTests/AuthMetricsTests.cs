using Iedora.Identity;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Iedora.Api.IntegrationTests;

// The auth business metrics on the Iedora.Identity Meter: signups (registrations) and token issuance.
[TestClass]
public sealed class AuthMetricsTests : IntegrationTestBase
{
    private const string Pw = "Sup3rSecret!";

    private static Task<List<(long Value, IReadOnlyDictionary<string, string?> Tags)>> Capture(string instrument, Func<Task> action) =>
        MetricsCapture.Counter(Telemetry.MeterName, instrument, action);

    [TestMethod]
    public async Task A_completed_registration_emits_a_created_signup_metric()
    {
        var m = await Capture(Telemetry.Instruments.Registrations, () => RegisterAccount("signup@m.pt", Pw));
        Assert.IsTrue(m.Any(x => x.Tags.GetValueOrDefault(Telemetry.Tags.Result) == Telemetry.Result.Created),
            "expected an iedora.auth.registrations measurement tagged result=created");
    }

    [TestMethod]
    public async Task A_duplicate_email_emits_a_rejected_signup_metric()
    {
        await RegisterAccount("dupe@m.pt", Pw); // first one succeeds
        var m = await Capture(Telemetry.Instruments.Registrations, async () => await Register("dupe@m.pt", Pw, "User"));
        Assert.IsTrue(m.Any(x => x.Tags.GetValueOrDefault(Telemetry.Tags.Result) == Telemetry.Result.Rejected),
            "expected an iedora.auth.registrations measurement tagged result=rejected");
    }

    [TestMethod]
    public async Task A_service_token_emits_a_tokens_issued_metric()
    {
        var m = await Capture(Telemetry.Instruments.TokensIssued, async () => await ServiceToken());
        Assert.IsTrue(m.Any(x => x.Tags.GetValueOrDefault(Telemetry.Tags.Grant) == Telemetry.Grant.ClientCredentials
                && x.Tags.GetValueOrDefault(Telemetry.Tags.Result) == Telemetry.Result.Issued),
            "expected an iedora.auth.tokens_issued measurement tagged grant=client_credentials, result=issued");
    }
}
