using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Framework.Commands;
using Framework.Web;
using Iedora.Kernel;
using Iedora.Data;

namespace Iedora.Tenancy;

public sealed record CreateTenantRequest([property: Required] string Name);

// POST /tenancy/tenants — the signed-in user provisions a tenant. Validated SYNCHRONOUSLY here,
// then accepted (202) and executed asynchronously: SubmitCommand stages a Pending command + an
// outbox message in one transaction; CreateTenantHandler (in the worker) does the write. The client
// polls the returned status URL for the outcome.
public static class CreateTenantEndpoint
{
    public static void MapTenants(this RouteGroupBuilder group) =>
        group.MapPost("/tenants", async (
                CreateTenantRequest req, ClaimsPrincipal principal,
                TenancyDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!principal.TryGetUserId(out var userId))
                return ProblemResults.From(CommonErrors.Unauthenticated);

            var commandId = Guid.CreateVersion7();
            db.SubmitCommand(commandId, CreateTenantCommand.Type, new CreateTenantCommand(req.Name, userId), clock);
            await db.SaveChangesAsync(ct); // command + outbox message, one transaction

            return CommandEndpoints.AcceptedCommand(commandId, "/tenancy");
        })
        .RequireAuthorization()
        .WithName("CreateTenant")
        .WithSummary("Provision a tenant owned by the signed-in user (async — poll the status URL).")
        .Produces<CommandAccepted>(StatusCodes.Status202Accepted)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
