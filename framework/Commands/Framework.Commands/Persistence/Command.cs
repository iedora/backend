namespace Framework.Commands;

public enum CommandStatus { Pending, Succeeded, Failed }

/// <summary>
/// Tracks the outcome of one asynchronous write. The accept endpoint stages it <c>Pending</c>
/// (see <c>SubmitCommand</c>); the <c>CommandHandler</c> flips it to <c>Succeeded</c> (+ a result
/// location) or <c>Failed</c> (+ an error code) when it runs. The client polls it via the status
/// endpoint. <see cref="Id"/> is client-supplied and doubles as the idempotency key.
/// </summary>
public sealed class Command
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    public string? ErrorCode { get; set; }
    public string? ErrorDetail { get; set; }
    public string? ResultLocation { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public void Succeed(string? resultLocation, DateTimeOffset now)
    {
        Status = CommandStatus.Succeeded;
        ResultLocation = resultLocation;
        CompletedAt = now;
    }

    public void Fail(string code, string detail, DateTimeOffset now)
    {
        Status = CommandStatus.Failed;
        ErrorCode = code;
        ErrorDetail = detail;
        CompletedAt = now;
    }
}
