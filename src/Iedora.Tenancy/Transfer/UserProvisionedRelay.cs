using System.Text.Json;
using Framework.Inbox;
using Framework.Outbox;
using Iedora.Contracts;
using Iedora.Data;

namespace Iedora.Tenancy;

/// <summary>The outbox→inbox bridge for hop 2 of the transfer saga. Identity publishes
/// <see cref="UserProvisioned"/> to ITS outbox; the worker dispatches it and hands it here, which
/// forwards it into Tenancy's <b>inbox</b> for idempotent processing by
/// <see cref="UserProvisionedInboxHandler"/>.</summary>
internal sealed class UserProvisionedRelay(InboxProcessor<TenancyDbContext> inbox) : IOutboxHandler
{
    public string Type => UserProvisioned.Type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<UserProvisioned>(payload)!;
        return inbox.ProcessOnceAsync(message.CorrelationId, Type, payload, ct);
    }
}
