using System.Text.Json;
using Framework.Email;
using Framework.Inbox;
using Framework.Notifications.Contracts;

namespace Framework.Notifications;

/// <summary>Delivers a queued <see cref="EmailRequested"/> over SMTP, inside the inbox transaction
/// (deduped on the correlation id, so redelivery never double-sends).</summary>
internal sealed class EmailRequestedInboxHandler(IEmailSender email) : IInboxHandler
{
    public string Type => EmailRequested.Type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<EmailRequested>(payload)!;
        return email.SendAsync(message.To, message.Subject, message.HtmlBody, ct);
    }
}
