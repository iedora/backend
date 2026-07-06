using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Framework.Web;
using Iedora.Auth.Common;
using Iedora.Auth.Data;

namespace Iedora.Auth.Features.Tenants;

public sealed record CreateTenantRequest([property: Required] string Name);

public sealed record CreateTenantResponse(Guid Id, string Name);

// POST /auth/tenants — the signed-in user provisions a tenant and becomes its owner. The tenant
// row + the owner membership are written in one SaveChanges (a single transaction). The caller's
// current access token doesn't carry the new tenant; the next login/refresh re-resolves the
// default tenant from memberships and pins it. Mirrors the Bun POST /auth/tenants.
public static class CreateTenantEndpoint
{
    public static void MapTenants(this RouteGroupBuilder group) =>
        group.MapPost("/tenants", async (
                CreateTenantRequest req, ClaimsPrincipal principal,
                AuthDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!Guid.TryParse(principal.FindFirstValue("sub"), out var userId))
                return ProblemResults.From(AuthErrors.UserGone);

            var now = clock.GetUtcNow();
            var tenant = new Tenant { Id = Guid.CreateVersion7(), Name = req.Name, CreatedAt = now };
            db.Tenants.Add(tenant);
            db.Memberships.Add(new Membership
            {
                UserId = userId,
                TenantId = tenant.Id,
                Role = MembershipRoles.Owner,
                CreatedAt = now,
            });
            await db.SaveChangesAsync(ct); // tenant + owner membership atomically

            return TypedResults.Ok(new CreateTenantResponse(tenant.Id, tenant.Name));
        })
        .RequireAuthorization()
        .WithName("CreateTenant")
        .WithSummary("Create a tenant owned by the signed-in user.")
        .Produces<CreateTenantResponse>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized);
}
