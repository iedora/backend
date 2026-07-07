using ErrorOr;
using Framework.Commands;
using Iedora.Data;

namespace Iedora.Tenancy;

/// <summary>Executes <see cref="CreateTenantCommand"/> off the outbox: writes the tenant + the owner
/// membership. The base commits the write together with the command's status flip.</summary>
internal sealed class CreateTenantHandler(TenancyDbContext db, TimeProvider clock)
    : CommandHandler<TenancyDbContext, CreateTenantCommand>(db, clock)
{
    public override string Type => CreateTenantCommand.Type;

    protected override Task<ErrorOr<string?>> ExecuteAsync(CreateTenantCommand cmd, CancellationToken ct)
    {
        var now = Clock.GetUtcNow();
        var tenant = new Tenant { Id = Guid.CreateVersion7(), Name = cmd.Name, CreatedAt = now };
        Db.Tenants.Add(tenant);
        Db.Memberships.Add(new Membership
        {
            UserId = cmd.OwnerUserId,
            TenantId = tenant.Id,
            Role = MembershipRole.Owner,
            CreatedAt = now,
        });
        return Task.FromResult<ErrorOr<string?>>($"/tenancy/tenants/{tenant.Id}");
    }
}
