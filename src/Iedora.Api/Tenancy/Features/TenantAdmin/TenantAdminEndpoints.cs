using System.ComponentModel.DataAnnotations;
using Framework.Web;
using Iedora.Api.Shared;
using Iedora.Api.Tenancy;
using Iedora.Api.Identity.Contracts;
using Iedora.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Api.Features.Tenants;

public sealed record OwnerSummary(Guid Id, string Email, string? Name);
public sealed record TenantWithOwner(Guid Id, string Name, OwnerSummary Owner);
public sealed record TenantListResponse(IReadOnlyList<TenantWithOwner> Tenants);

public sealed record AdminCreateTenantRequest([property: Required] string Name, Guid OwnerUserId);

// GET /tenancy/admin/tenants[/{id}] — admin-only reads of tenants joined to their owner. The owner
// is an Identity-module user, so it's resolved through IIdentityApi (never by querying identity
// tables). Tenants without a resolvable owner are omitted. Feeds the staff/admin "tenants" views.
public static class TenantAdminEndpoints
{
    public static void MapTenantAdmin(this RouteGroupBuilder group)
    {
        var admin = group.MapGroup("/admin/tenants").RequireAuthorization(Policies.Admin);

        admin.MapGet("/", async (TenancyDbContext db, IIdentityApi identity, CancellationToken ct) =>
            {
                var owned = await OwnedTenantsAsync(db, id: null, ct);
                return TypedResults.Ok(new TenantListResponse(await HydrateAsync(owned, identity, ct)));
            })
            .WithName("ListTenants")
            .WithSummary("List all tenants with their owner (admin).");

        admin.MapGet("/{id:guid}", async Task<Results<Ok<TenantWithOwner>, NotFound>> (
                Guid id, TenancyDbContext db, IIdentityApi identity, CancellationToken ct) =>
            {
                var owned = await OwnedTenantsAsync(db, id, ct);
                var hydrated = await HydrateAsync(owned, identity, ct);
                return hydrated.Count == 1 ? TypedResults.Ok(hydrated[0]) : TypedResults.NotFound();
            })
            .WithName("GetTenant")
            .WithSummary("Get a tenant with its owner (admin).");

        // Admin provisions a tenant owned by an EXISTING user (distinct from the user-facing
        // POST /tenancy/tenants, which owns it by the caller). The owner must be a real user —
        // verified through IIdentityApi, since Identity owns users.
        admin.MapPost("/", async (
                AdminCreateTenantRequest req, TenancyDbContext db, IIdentityApi identity,
                TimeProvider clock, CancellationToken ct) =>
            {
                var owner = (await identity.GetUsersAsync([req.OwnerUserId], ct)).FirstOrDefault();
                if (owner is null) return ProblemResults.From(TenancyErrors.UnknownOwner);

                var now = clock.GetUtcNow();
                var tenant = new Tenant { Id = Guid.CreateVersion7(), Name = req.Name, CreatedAt = now };
                db.Tenants.Add(tenant);
                db.Memberships.Add(new Membership
                {
                    UserId = req.OwnerUserId,
                    TenantId = tenant.Id,
                    Role = MembershipRole.Owner,
                    CreatedAt = now,
                });
                await db.SaveChangesAsync(ct); // tenant + owner membership atomically

                return TypedResults.Ok(new CreateTenantResponse(tenant.Id, tenant.Name));
            })
            .WithName("AdminCreateTenant")
            .WithSummary("Provision a tenant owned by an existing user (admin).")
            .Produces<CreateTenantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private sealed record OwnedTenant(Guid Id, string Name, Guid OwnerId);

    // Tenants (all, or one by id) joined to their owner membership — one translatable query within
    // Tenancy's schema. Filter + order happen on the entities, before the projection.
    private static Task<List<OwnedTenant>> OwnedTenantsAsync(TenancyDbContext db, Guid? id, CancellationToken ct) =>
        (from t in db.Tenants
         where id == null || t.Id == id
         join m in db.Memberships on t.Id equals m.TenantId
         where m.Role == MembershipRole.Owner
         orderby t.Name
         select new OwnedTenant(t.Id, t.Name, m.UserId))
        .ToListAsync(ct);

    // Resolve the owner users from the Identity module and stitch them in.
    private static async Task<List<TenantWithOwner>> HydrateAsync(
        List<OwnedTenant> owned, IIdentityApi identity, CancellationToken ct)
    {
        if (owned.Count == 0) return [];
        var owners = (await identity.GetUsersAsync(owned.Select(o => o.OwnerId).Distinct().ToList(), ct))
            .ToDictionary(u => u.Id);
        return owned
            .Where(o => owners.ContainsKey(o.OwnerId))
            .Select(o =>
            {
                var u = owners[o.OwnerId];
                return new TenantWithOwner(o.Id, o.Name, new OwnerSummary(u.Id, u.Email, u.Name));
            })
            .ToList();
    }
}
