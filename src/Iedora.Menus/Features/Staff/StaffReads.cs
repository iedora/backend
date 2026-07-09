using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// Cross-tenant read models for the staff oversight console (ported from data/staff.ts). Every query
// spans all tenants by design (platform-staff oversight); pure reads. Per-restaurant counts come from
// three grouped queries (one GROUP BY scan each, keyed on the already-selected ids) merged in memory
// — not a correlated subquery per row.

/// <summary>A restaurant as the staff console lists it: identity + content counts + 30-day reach.</summary>
public sealed record StaffRestaurantRow(
    Guid Id, Guid TenantId, string Name, string Slug, int Menus, int Items, int Views30d, DateTimeOffset CreatedAt);

internal static class StaffReads
{
    /// <summary>Inclusive start of the trailing 30-day window (29 days before today).</summary>
    public static DateOnly Since30(DateOnly today) => today.AddDays(-29);

    /// <summary>Materialize the given restaurants to staff rows. The caller filters/orders/limits the
    /// source; this selects those rows, then attaches menu/item/30-day-view counts with one grouped
    /// query each (keyed on the selected ids). Row order is preserved.</summary>
    public static async Task<List<StaffRestaurantRow>> ProjectAsync(
        IQueryable<Restaurant> restaurants, MenuDbContext db, DateOnly since, CancellationToken ct)
    {
        var rows = await restaurants
            .Select(r => new { r.Id, r.TenantId, r.Name, r.Slug, r.CreatedAt }).ToListAsync(ct);
        if (rows.Count == 0) return [];
        var ids = rows.Select(r => r.Id).ToList();

        var menus = (await db.Menus.AsNoTracking().Where(m => ids.Contains(m.RestaurantId))
            .GroupBy(m => m.RestaurantId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
        var items = (await db.Items.AsNoTracking().Where(i => ids.Contains(i.RestaurantId))
            .GroupBy(i => i.RestaurantId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);
        var views = (await db.DailyViews.AsNoTracking().Where(d => ids.Contains(d.RestaurantId) && d.Day >= since)
            .GroupBy(d => d.RestaurantId).Select(g => new { g.Key, N = g.Sum(x => x.Count) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.N);

        return rows.Select(r => new StaffRestaurantRow(
            r.Id, r.TenantId, r.Name, r.Slug,
            menus.GetValueOrDefault(r.Id), items.GetValueOrDefault(r.Id), views.GetValueOrDefault(r.Id),
            r.CreatedAt)).ToList();
    }
}
