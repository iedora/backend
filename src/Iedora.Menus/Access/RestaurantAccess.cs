using System.Security.Claims;
using Iedora.Data;
using Iedora.Kernel;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

/// <summary>
/// Scoped access to a restaurant addressed by slug: the caller's tenant must own it, unless the
/// caller is platform staff (the <see cref="Roles.Admin"/> role — so a staff member can manage any
/// tenant's restaurant). A foreign restaurant is indistinguishable from a missing one (both return
/// null → 404), so ownership isn't enumerable.
/// </summary>
internal static class RestaurantAccess
{
    public static async Task<Restaurant?> LoadAsync(
        MenuDbContext db, ClaimsPrincipal caller, string slug, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
        if (restaurant is null) return null;

        var owns = caller.TenantId() is { } tenant && tenant == restaurant.TenantId;
        return owns || caller.IsInRole(Roles.Admin) ? restaurant : null;
    }
}
