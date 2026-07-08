namespace Iedora.Menus;

// Cross-tenant read models for the staff oversight console (ported from data/staff.ts). Every query
// spans all tenants by design (platform-staff oversight); pure reads. Row counts come from correlated
// subqueries — one SQL statement per list, fine at admin volumes.

/// <summary>A restaurant as the staff console lists it: identity + content counts + 30-day reach.</summary>
public sealed record StaffRestaurantRow(
    Guid Id, Guid TenantId, string Name, string Slug, int Menus, int Items, int Views30d, DateTimeOffset CreatedAt);

internal static class StaffReads
{
    /// <summary>Inclusive start of the trailing 30-day window (29 days before today, UTC).</summary>
    public static DateOnly Since30(DateOnly today) => today.AddDays(-29);

    /// <summary>Project restaurants to the staff row. The caller filters/orders/limits the source
    /// first; this only attaches the per-restaurant counts.</summary>
    public static IQueryable<StaffRestaurantRow> Project(
        IQueryable<Restaurant> restaurants, MenuDbContext db, DateOnly since) =>
        restaurants.Select(r => new StaffRestaurantRow(
            r.Id, r.TenantId, r.Name, r.Slug,
            db.Menus.Count(m => m.RestaurantId == r.Id),
            db.Items.Count(i => i.RestaurantId == r.Id),
            db.DailyViews.Where(d => d.RestaurantId == r.Id && d.Day >= since).Sum(d => (int?)d.Count) ?? 0,
            r.CreatedAt));
}
