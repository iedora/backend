using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// A restaurant's menus with their category + dish counts — three flat grouped queries, no N+1.
// Shared by the owner overview (RestaurantReads) and the staff restaurant detail (StaffConsole).
internal static class MenuSummaries
{
    public static async Task<List<MenuSummary>> ForRestaurantAsync(
        MenuDbContext db, Guid restaurantId, CancellationToken ct)
    {
        var menus = await db.Menus.AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.Position).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var menuIds = menus.Select(m => m.Id).ToList();

        var categoryCounts = (await db.Categories.AsNoTracking()
            .Where(c => menuIds.Contains(c.MenuId))
            .GroupBy(c => c.MenuId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);
        var dishCounts = (await (
            from i in db.Items.AsNoTracking()
            join c in db.Categories.AsNoTracking() on i.CategoryId equals c.Id
            where menuIds.Contains(c.MenuId)
            group i by c.MenuId into g
            select new { g.Key, Count = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Count);

        return menus.Select(m => new MenuSummary(
            m.Id, m.Name, m.Active, m.Position, m.UpdatedAt,
            categoryCounts.GetValueOrDefault(m.Id), dishCounts.GetValueOrDefault(m.Id))).ToList();
    }
}
