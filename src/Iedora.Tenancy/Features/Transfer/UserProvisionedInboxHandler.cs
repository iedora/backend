using System.Text.Json;
using Framework.Commands;
using Framework.Inbox;
using Iedora.Identity.Contracts;
using Iedora.Tenancy.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Tenancy;

/// <summary>
/// Hop 2 of the transfer saga, inside Tenancy's inbox transaction. On success: make the new user the
/// tenant's sole owner (drop current owner(s), add the new) and mark the tracking command
/// <c>Succeeded</c>. On failure (e.g. the email was taken): mark it <c>Failed</c> with the code. The
/// ownership swap + the command completion commit together with the inbox dedup row.
/// </summary>
internal sealed class UserProvisionedInboxHandler(TenancyDbContext db, TimeProvider clock) : IInboxHandler
{
    public string Type => UserProvisioned.Type;

    public async Task HandleAsync(string payload, CancellationToken ct)
    {
        var message = JsonSerializer.Deserialize<UserProvisioned>(payload)!;

        var command = await db.Set<Command>().FindAsync([message.CorrelationId], ct);
        if (command is null || command.Status != CommandStatus.Pending)
            return; // unknown, or already completed — idempotent no-op

        var now = clock.GetUtcNow();
        if (message.UserId is { } newOwnerId)
        {
            var currentOwners = await db.Memberships
                .Where(m => m.TenantId == message.TenantId && m.Role == MembershipRole.Owner)
                .ToListAsync(ct);
            db.Memberships.RemoveRange(currentOwners);
            db.Memberships.Add(new Membership
            {
                UserId = newOwnerId,
                TenantId = message.TenantId,
                Role = MembershipRole.Owner,
                CreatedAt = now,
            });
            command.Succeed($"/tenancy/admin/tenants/{message.TenantId}", now);
        }
        else
        {
            command.Fail(message.ErrorCode ?? "tenancy.transfer_failed", "The tenant transfer failed.", now);
        }

        await db.SaveChangesAsync(ct);
    }
}
