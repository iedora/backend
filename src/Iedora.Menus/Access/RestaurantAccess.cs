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
    /// <summary>Load a restaurant for a READ (no change tracking), authorized for the caller.</summary>
    public static async Task<Restaurant?> LoadAsync(
        MenuDbContext db, ClaimsPrincipal caller, string slug, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
        return restaurant is not null && CanAccess(caller, restaurant) ? restaurant : null;
    }

    /// <summary>Load a restaurant TRACKED for a write (mutate + SaveChanges), authorized for the caller.</summary>
    public static async Task<Restaurant?> LoadTrackedAsync(
        MenuDbContext db, ClaimsPrincipal caller, string slug, CancellationToken ct)
    {
        var restaurant = await db.Restaurants.FirstOrDefaultAsync(r => r.Slug == slug, ct);
        return restaurant is not null && CanAccess(caller, restaurant) ? restaurant : null;
    }

    public static bool CanAccess(ClaimsPrincipal caller, Restaurant restaurant) =>
        (caller.TenantId() is { } tenant && tenant == restaurant.TenantId) || caller.IsInRole(Roles.Admin);
}
