using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Framework.Maintenance.Tests;

[TestClass]
public sealed class RetentionSweepServiceTests
{
    // A sweep that records each invocation, returns a fixed row count, and can be told to throw.
    private sealed class FakeSweep(string name, int rows = 0, bool throws = false) : IRetentionSweep
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Name => name;

        public Task<int> SweepAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return throws ? throw new InvalidOperationException("boom") : Task.FromResult(rows);
        }
    }

    private static (RetentionSweepService svc, FakeTimeProvider clock) Build(
        int intervalMinutes, params IRetentionSweep[] sweeps)
    {
        var services = new ServiceCollection();
        foreach (var s in sweeps) services.AddScoped(_ => s);
        var provider = services.BuildServiceProvider();
        var clock = new FakeTimeProvider();
        var svc = new RetentionSweepService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new RetentionOptions { IntervalMinutes = intervalMinutes }),
            clock, NullLogger<RetentionSweepService>.Instance);
        return (svc, clock);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.IsTrue(condition(), $"timed out waiting for {what}");
    }

    [TestMethod]
    public async Task Runs_every_sweep_and_a_failure_does_not_stop_the_rest()
    {
        var a = new FakeSweep("a", rows: 2);
        var bad = new FakeSweep("bad", throws: true);
        var c = new FakeSweep("c", rows: 0);
        var (svc, _) = Build(60, a, bad, c);

        await svc.SweepAllAsync(CancellationToken.None); // must not throw despite `bad`

        Assert.AreEqual(1, a.Calls);
        Assert.AreEqual(1, bad.Calls);
        Assert.AreEqual(1, c.Calls); // reached even though the sweep before it threw
    }

    [TestMethod]
    public async Task Sweeps_once_at_startup_and_again_on_each_interval()
    {
        var a = new FakeSweep("a");
        var (svc, clock) = Build(60, a);

        await svc.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => a.Calls == 1, "the startup sweep");

        clock.Advance(TimeSpan.FromMinutes(60));
        await WaitUntilAsync(() => a.Calls == 2, "the first interval sweep");

        clock.Advance(TimeSpan.FromMinutes(60));
        await WaitUntilAsync(() => a.Calls == 3, "the second interval sweep");

        await svc.StopAsync(CancellationToken.None);
    }
}
