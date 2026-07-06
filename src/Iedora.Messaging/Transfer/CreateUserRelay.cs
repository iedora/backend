using System.Text.Json;
using Framework.Inbox;
using Framework.Outbox;
using Iedora.Contracts;
using Iedora.Data;

namespace Iedora.Messaging;

/// <summary>
/// The outbox→inbox bridge for hop 1 of the transfer saga. Tenancy publishes
/// <see cref="CreateUserRequested"/> to ITS outbox; the worker dispatches it and hands it here,
/// which forwards it into Identity's <b>inbox</b> (dedup on the correlation id) for idempotent
/// processing by <see cref="CreateUserInboxHandler"/>.
/// </summary>
internal sealed class CreateUserRelay(InboxProcessor<IdentityDbContext> inbox) : IOutboxHandler
{
    public string Type => CreateUserRequested.Type;

    public Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<CreateUserRequested>(payload)!;
        return inbox.ProcessOnceAsync(message.CorrelationId, Type, payload, ct);
    }
}
