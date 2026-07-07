using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Framework.Outbox.Tests;

// A throwaway DbContext that only maps the outbox — proves the library works with ANY DbContext,
// independent of any service. Backed by a real Postgres (Testcontainers), like production.
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) => builder.MapOutbox();
}

/// <summary>A handler that records the payloads it received, or throws if told to fail.</summary>
public sealed class RecordingHandler(string type, bool fail = false) : IOutboxHandler
{
    public List<string> Handled { get; } = [];
    public string Type => type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        if (fail) throw new InvalidOperationException("boom");
        Handled.Add(payload);
        return Task.CompletedTask;
    }
}

[TestClass]
public sealed class OutboxProcessorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static PostgreSqlContainer _pg = null!;
    private static DbContextOptions<TestDbContext> _options = null!;
    private FakeTimeProvider _clock = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        _pg = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _pg.StartAsync(context.CancellationTokenSource.Token);
        _options = new DbContextOptionsBuilder<TestDbContext>()
            .UseNpgsql(_pg.GetConnectionString())
            .UseOutboxNotifications()
            .Options;
        await using var db = new TestDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup() => await _pg.DisposeAsync();

    [TestInitialize]
    public async Task Init()
    {
        await using var db = new TestDbContext(_options);
        await db.Set<OutboxMessage>().ExecuteDeleteAsync(); // fresh per test
        _clock = new FakeTimeProvider(Now);
    }

    private static TestDbContext NewDb() => new(_options);

    private OutboxProcessor<TestDbContext> Processor(TestDbContext db, int maxAttempts, int batchSize, params IOutboxHandler[] handlers) =>
        new(db, handlers, _clock,
            Options.Create(new OutboxOptions { MaxAttempts = maxAttempts, BatchSize = batchSize }),
            NullLogger<OutboxProcessor<TestDbContext>>.Instance);

    private async Task EnqueueAsync(string type, object payload)
    {
        await using var db = NewDb();
        db.EnqueueOutbox(type, payload, _clock);
        await db.SaveChangesAsync();
    }

    private async Task<OutboxMessage> SingleAsync()
    {
        await using var db = NewDb();
        return await db.Set<OutboxMessage>().SingleAsync();
    }

    [TestMethod]
    public async Task Enqueue_fires_a_NOTIFY_on_the_outbox_channel()
    {
        // TestDbContext has no default schema, so its wake-up channel is "outbox_public".
        await using var listener = new NpgsqlConnection(_pg.GetConnectionString());
        await listener.OpenAsync();
        string? channel = null;
        listener.Notification += (_, e) => channel = e.Channel;
        await using (var listen = new NpgsqlCommand("LISTEN outbox_public", listener))
            await listen.ExecuteNonQueryAsync();

        // The interceptor should emit pg_notify in the same commit as the outbox insert.
        await EnqueueAsync("test", new { hello = "world" });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await listener.WaitAsync(timeout.Token); // returns when the NOTIFY lands (throws if it never does)
        Assert.AreEqual("outbox_public", channel);
    }

    [TestMethod]
    public async Task Dispatches_pending_to_the_matching_handler_and_marks_it_processed()
    {
        await EnqueueAsync("test", new { hello = "world" });
        var handler = new RecordingHandler("test");

        await using (var db = NewDb())
            Assert.AreEqual(1, await Processor(db, 3, 10, handler).DispatchPendingAsync(CancellationToken.None));

        Assert.HasCount(1, handler.Handled);
        Assert.IsNotNull((await SingleAsync()).ProcessedAt);
    }

    [TestMethod]
    public async Task Failing_handler_increments_attempts_records_error_and_backs_off()
    {
        await EnqueueAsync("test", new { });

        await using (var db = NewDb())
            Assert.AreEqual(0, await Processor(db, 3, 10, new RecordingHandler("test", fail: true)).DispatchPendingAsync(CancellationToken.None));

        var msg = await SingleAsync();
        Assert.IsNull(msg.ProcessedAt);
        Assert.AreEqual(1, msg.Attempts);
        Assert.AreEqual("boom", msg.LastError);
        Assert.IsNotNull(msg.NextAttemptAt);
        Assert.IsTrue(msg.NextAttemptAt > Now); // pushed into the future
    }

    [TestMethod]
    public async Task Backoff_gates_redispatch_until_the_next_attempt_time()
    {
        await EnqueueAsync("test", new { });
        await using (var db = NewDb())
            await Processor(db, 3, 10, new RecordingHandler("test", fail: true)).DispatchPendingAsync(CancellationToken.None);

        // A working handler still can't run yet — the message is backed off.
        await using (var db = NewDb())
            Assert.AreEqual(0, await Processor(db, 3, 10, new RecordingHandler("test")).DispatchPendingAsync(CancellationToken.None));

        _clock.Advance(TimeSpan.FromSeconds(30)); // past the backoff window

        await using (var db = NewDb())
            Assert.AreEqual(1, await Processor(db, 3, 10, new RecordingHandler("test")).DispatchPendingAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task Stops_retrying_after_MaxAttempts()
    {
        await EnqueueAsync("test", new { });
        await using (var db = NewDb())
            await Processor(db, maxAttempts: 1, 10, new RecordingHandler("test", fail: true)).DispatchPendingAsync(CancellationToken.None);

        _clock.Advance(TimeSpan.FromMinutes(10)); // even past any backoff, it's exhausted

        await using (var db = NewDb())
            Assert.AreEqual(0, await Processor(db, maxAttempts: 1, 10, new RecordingHandler("test")).DispatchPendingAsync(CancellationToken.None));
        Assert.AreEqual(1, (await SingleAsync()).Attempts);
    }

    [TestMethod]
    public async Task Unknown_message_type_is_recorded_as_a_failure_not_thrown()
    {
        await EnqueueAsync("no-handler-for-this", new { });

        await using (var db = NewDb())
            Assert.AreEqual(0, await Processor(db, 3, 10, new RecordingHandler("something-else")).DispatchPendingAsync(CancellationToken.None));

        var msg = await SingleAsync();
        Assert.AreEqual(1, msg.Attempts);
        Assert.IsNotNull(msg.LastError);
    }

    [TestMethod]
    public async Task Routes_each_message_to_its_own_handler_by_type()
    {
        await EnqueueAsync("a", new { });
        await EnqueueAsync("b", new { });
        var a = new RecordingHandler("a");
        var b = new RecordingHandler("b");

        await using (var db = NewDb())
            Assert.AreEqual(2, await Processor(db, 3, 10, a, b).DispatchPendingAsync(CancellationToken.None));

        Assert.HasCount(1, a.Handled);
        Assert.HasCount(1, b.Handled);
    }

    [TestMethod]
    public async Task Dispatches_at_most_BatchSize_per_call()
    {
        for (var i = 0; i < 5; i++) await EnqueueAsync("test", new { i });

        await using (var db = NewDb())
            Assert.AreEqual(2, await Processor(db, 3, batchSize: 2, new RecordingHandler("test")).DispatchPendingAsync(CancellationToken.None));

        await using var check = NewDb();
        Assert.AreEqual(3, await check.Set<OutboxMessage>().CountAsync(m => m.ProcessedAt == null));
    }
}
