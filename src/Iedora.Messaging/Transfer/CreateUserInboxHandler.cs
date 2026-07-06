using System.Text.Json;
using Framework.Inbox;
using Framework.Outbox;
using Iedora.Contracts;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Messaging;

/// <summary>
/// Hop 1 of the transfer saga, inside Identity's inbox transaction: create the new owner user, then
/// emit <see cref="UserProvisioned"/> (success or failure) back to Identity's outbox. The user is
/// created WITHOUT a password (they set one via the reset flow) — so no plaintext password ever
/// travels through the event tables. Deduped by the inbox on the correlation id.
/// </summary>
internal sealed class CreateUserInboxHandler(IdentityDbContext db, UserManager<AppUser> users, TimeProvider clock)
    : IInboxHandler
{
    public string Type => CreateUserRequested.Type;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<CreateUserRequested>(payload)!;

        var user = new AppUser { UserName = message.Email, Email = message.Email, DisplayName = message.Name };
        var result = await users.CreateAsync(user); // passwordless — reset flow sets it

        // An expected failure (e.g. email taken) is NOT a retry — it's a saga outcome we report back.
        var outcome = result.Succeeded
            ? new UserProvisioned(message.CorrelationId, message.TenantId, user.Id, null)
            : new UserProvisioned(message.CorrelationId, message.TenantId, null,
                result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName")
                    ? "auth.email_taken"
                    : "auth.user_create_failed");

        db.EnqueueOutbox(UserProvisioned.Type, outcome, clock);
        await db.SaveChangesAsync(ct); // persist the outbox event inside the inbox transaction
    }
}
