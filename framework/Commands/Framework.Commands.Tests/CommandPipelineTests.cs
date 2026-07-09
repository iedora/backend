using System.Diagnostics.Metrics;
using System.Text.Json;
using ErrorOr;
using Framework.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace Framework.Commands.Tests;

// A throwaway DbContext mapping the command table + the outbox — proves the pipeline works with any
// DbContext, on a real Postgres (Testcontainers), like production.
public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.MapCommands();
        builder.MapOutbox();
    }
}

public sealed record TestPayload(string Value);

// A command handler whose ExecuteAsync succeeds with a result location, or returns an expected error.
public sealed class TestCommandHandler(TestDbContext db, TimeProvider clock, bool fail = false)
    : CommandHandler<TestDbContext, TestPayload>(db, clock)
{
    public int Executions { get; private set; }
    public override string Type => "test.command";

    protected override Task<ErrorOr<string?>> ExecuteAsync(TestPayload data, CancellationToken ct)
    {
        Executions++;
        return Task.FromResult<ErrorOr<string?>>(
            fail ? Error.Validation("test.rejected", "nope") : $"/things/{data.Value}");
    }
}

[TestClass]
public sealed class CommandPipelineTests
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
        await db.Set<Command>().ExecuteDeleteAsync();
        await db.Set<OutboxMessage>().ExecuteDeleteAsync();
        _clock = new FakeTimeProvider(Now);
    }

    private TestDbContext NewDb() => new(_options);

    private OutboxProcessor<TestDbContext> Processor(TestDbContext db, params IOutboxHandler[] handlers) =>
        new(db, handlers, _clock,
            Options.Create(new OutboxOptions { MaxAttempts = 5, BatchSize = 10 }),
            NullLogger<OutboxProcessor<TestDbContext>>.Instance);

    [TestMethod]
    public async Task SubmitCommand_stages_a_pending_command_and_an_outbox_message_atomically()
    {
        var id = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.SubmitCommand(id, "test.command", new TestPayload("x"), _clock);
            await db.SaveChangesAsync();
        }

        await using var check = NewDb();
        var command = await check.Set<Command>().SingleAsync();
        Assert.AreEqual(id, command.Id);
        Assert.AreEqual(CommandStatus.Pending, command.Status);
        Assert.AreEqual(1, await check.Set<OutboxMessage>().CountAsync());
    }

    [TestMethod]
    public async Task Dispatching_runs_the_handler_and_marks_the_command_succeeded()
    {
        var id = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.SubmitCommand(id, "test.command", new TestPayload("42"), _clock);
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            var handled = await Processor(db, new TestCommandHandler(db, _clock)).DispatchPendingAsync(CancellationToken.None);
            Assert.AreEqual(1, handled);
        }

        await using var check = NewDb();
        var command = await check.Set<Command>().SingleAsync();
        Assert.AreEqual(CommandStatus.Succeeded, command.Status);
        Assert.AreEqual("/things/42", command.ResultLocation);
        Assert.IsNull(command.ErrorCode);
    }

    [TestMethod]
    public async Task An_expected_error_marks_the_command_failed_with_its_code()
    {
        var id = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.SubmitCommand(id, "test.command", new TestPayload("x"), _clock);
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
            await Processor(db, new TestCommandHandler(db, _clock, fail: true)).DispatchPendingAsync(CancellationToken.None);

        await using var check = NewDb();
        var command = await check.Set<Command>().SingleAsync();
        Assert.AreEqual(CommandStatus.Failed, command.Status);
        Assert.AreEqual("test.rejected", command.ErrorCode);
    }

    [TestMethod]
    public async Task A_failed_command_increments_the_failed_metric_tagged_by_command_and_code()
    {
        var id = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.SubmitCommand(id, "test.command", new TestPayload("x"), _clock);
            await db.SaveChangesAsync();
        }

        var captured = new List<Dictionary<string, string?>>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (i, l) =>
            {
                if (i.Meter.Name == CommandsTelemetry.MeterName && i.Name == CommandsTelemetry.Instruments.Failed)
                    l.EnableMeasurementEvents(i);
            },
        };
        listener.SetMeasurementEventCallback<long>((i, m, tags, _) =>
        {
            var d = new Dictionary<string, string?>();
            foreach (var t in tags) d[t.Key] = t.Value as string;
            captured.Add(d);
        });
        listener.Start();

        await using (var db = NewDb())
            await Processor(db, new TestCommandHandler(db, _clock, fail: true)).DispatchPendingAsync(CancellationToken.None);

        listener.Dispose(); // flush
        Assert.IsTrue(
            captured.Any(t => t.GetValueOrDefault(CommandsTelemetry.Tags.Command) == "test.command"
                && t.GetValueOrDefault(CommandsTelemetry.Tags.Code) == "test.rejected"),
            "expected a commands.failed measurement tagged command=test.command, code=test.rejected");
    }

    [TestMethod]
    public async Task A_terminal_command_is_not_re_executed()
    {
        var id = Guid.NewGuid();
        await using (var db = NewDb())
        {
            db.Set<Command>().Add(new Command { Id = id, Type = "test.command", Status = CommandStatus.Succeeded, CreatedAt = Now });
            await db.SaveChangesAsync();
        }

        await using var db2 = NewDb();
        var handler = new TestCommandHandler(db2, _clock);
        await handler.HandleAsync(
            JsonSerializer.Serialize(new CommandEnvelope<TestPayload>(id, new TestPayload("x"))),
            CancellationToken.None);

        Assert.AreEqual(0, handler.Executions); // skipped — idempotent under redelivery
    }
}
