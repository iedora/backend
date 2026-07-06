using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Testcontainers.PostgreSql;

namespace Framework.Inbox.Tests;

// A throwaway DbContext that only maps the inbox — proves the library works with ANY DbContext,
// independent of any service. Backed by a real Postgres (Testcontainers).
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder) => builder.MapInbox();
}

public sealed class RecordingHandler(string type, bool fail = false) : IInboxHandler
{
    public int Calls { get; private set; }
    public string Type => type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        if (fail) throw new InvalidOperationException("boom");
        Calls++;
        return Task.CompletedTask;
    }
}

[TestClass]
public sealed class InboxProcessorTests
{
    private static PostgreSqlContainer _pg = null!;
    private static DbContextOptions<TestDbContext> _options = null!;
    private FakeTimeProvider _clock = null!;

    [ClassInitialize]
    public static async Task ClassInit(TestContext context)
    {
        _pg = new PostgreSqlBuilder("postgres:18-alpine").Build();
        await _pg.StartAsync(context.CancellationTokenSource.Token);
        _options = new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(_pg.GetConnectionString()).Options;
        await using var db = new TestDbContext(_options);
        await db.Database.EnsureCreatedAsync();
    }

    [ClassCleanup]
    public static async Task ClassCleanup() => await _pg.DisposeAsync();

    [TestInitialize]
    public async Task Init()
    {
        await using var db = new TestDbContext(_options);
        await db.Set<InboxMessage>().ExecuteDeleteAsync();
        _clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero));
    }

    private static TestDbContext NewDb() => new(_options);

    private static InboxProcessor<TestDbContext> Processor(TestDbContext db, FakeTimeProvider clock, params IInboxHandler[] handlers) =>
        new(db, handlers, clock);

    [TestMethod]
    public async Task First_receipt_runs_the_handler_and_records_the_message()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler("test");

        bool handled;
        await using (var db = NewDb())
            handled = await Processor(db, _clock, handler).ProcessOnceAsync(id, "test", "{}", CancellationToken.None);

        Assert.IsTrue(handled);
        Assert.AreEqual(1, handler.Calls);
        await using var check = NewDb();
        Assert.AreEqual(1, await check.Set<InboxMessage>().CountAsync(m => m.MessageId == id));
    }

    [TestMethod]
    public async Task Duplicate_message_id_is_skipped_and_the_handler_runs_only_once()
    {
        var id = Guid.NewGuid();
        var handler = new RecordingHandler("test");

        await using (var db = NewDb())
            Assert.IsTrue(await Processor(db, _clock, handler).ProcessOnceAsync(id, "test", "{}", CancellationToken.None));
        await using (var db = NewDb())
            Assert.IsFalse(await Processor(db, _clock, handler).ProcessOnceAsync(id, "test", "{}", CancellationToken.None)); // redelivery

        Assert.AreEqual(1, handler.Calls); // idempotent
    }

    [TestMethod]
    public async Task A_failing_handler_rolls_back_so_the_message_stays_retryable()
    {
        var id = Guid.NewGuid();

        await using (var db = NewDb())
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                Processor(db, _clock, new RecordingHandler("test", fail: true)).ProcessOnceAsync(id, "test", "{}", CancellationToken.None));

        // Nothing recorded — the dedup row rolled back with the failed handler.
        await using (var db = NewDb())
            Assert.AreEqual(0, await db.Set<InboxMessage>().CountAsync());

        // A retry with a working handler now succeeds.
        var ok = new RecordingHandler("test");
        await using (var db = NewDb())
            Assert.IsTrue(await Processor(db, _clock, ok).ProcessOnceAsync(id, "test", "{}", CancellationToken.None));
        Assert.AreEqual(1, ok.Calls);
    }

    [TestMethod]
    public async Task Routes_to_the_handler_matching_the_message_type()
    {
        var a = new RecordingHandler("a");
        var b = new RecordingHandler("b");

        await using (var db = NewDb())
            await Processor(db, _clock, a, b).ProcessOnceAsync(Guid.NewGuid(), "b", "{}", CancellationToken.None);

        Assert.AreEqual(0, a.Calls);
        Assert.AreEqual(1, b.Calls);
    }

    [TestMethod]
    public async Task Throws_when_no_handler_is_registered_for_the_type()
    {
        await using var db = NewDb();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            Processor(db, _clock, new RecordingHandler("known")).ProcessOnceAsync(Guid.NewGuid(), "unknown", "{}", CancellationToken.None));
    }
}
