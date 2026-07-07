using System.Text.Json;
using Framework.Inbox;
using Framework.Outbox;
using Iedora.Notifications.Contracts;

namespace Iedora.Notifications;

/// <summary>The outbox→inbox bridge: a publisher's OutboxProcessor dispatches <see cref="EmailRequested"/>
/// (matched by Type across the global handler pool) and hands it here, which forwards it into the
/// Notifications INBOX (dedup on the correlation id) for idempotent delivery by
/// <see cref="EmailRequestedInboxHandler"/>.</summary>
internal sealed class EmailRequestedRelay(InboxProcessor<NotificationsDbContext> inbox) : IOutboxHandler
{
    public string Type => EmailRequested.Type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<EmailRequested>(payload)!;
        return inbox.ProcessOnceAsync(message.CorrelationId, Type, payload, ct);
    }
}
