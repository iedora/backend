using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// The owner/staff editor's read model. Unlike the public render, the tree here is RAW — i18n maps
// intact, inactive menus and unavailable items included — because the builder edits all of it.

public sealed record MenuSummary(
    Guid Id, string Name, bool Active, int Position, DateTimeOffset UpdatedAt, int CategoryCount, int DishCount);

public sealed record RestaurantDetail(
    Guid Id, Guid TenantId, string Name, string Slug, string? Description, LocalizedText? DescriptionI18n,
    string? LogoUrl, string? BannerUrl, Theme? Theme, string DefaultLanguage,
    IReadOnlyList<string> SupportedLanguages, string DefaultCurrency,
    DateTimeOffset? OnboardingCompletedAt, DateTimeOffset UpdatedAt)
{
    public static RestaurantDetail From(Restaurant r) => new(
        r.Id, r.TenantId, r.Name, r.Slug, r.Description, r.DescriptionI18n, r.LogoUrl, r.BannerUrl,
        r.Theme, r.DefaultLanguage, r.SupportedLanguages, r.DefaultCurrency, r.OnboardingCompletedAt, r.UpdatedAt);
}

public sealed record RestaurantOverview(RestaurantDetail Restaurant, IReadOnlyList<MenuSummary> Menus);

public sealed record TreeVariant(string Label, LocalizedText? LabelI18n, int PriceCents);
public sealed record TreeItem(
    Guid Id, Guid CategoryId, string Name, LocalizedText? NameI18n, string? Description,
    LocalizedText? DescriptionI18n, int PriceCents, string Currency, string? ImageUrl, int Position,
    bool Available, IReadOnlyList<string> Tags, IReadOnlyList<TreeVariant> Variants);
public sealed record TreeCategory(
    Guid Id, Guid MenuId, string Name, LocalizedText? NameI18n, string? Description,
    LocalizedText? DescriptionI18n, int Position, IReadOnlyList<TreeItem> Items);
public sealed record TreeMenu(
    Guid Id, string Name, LocalizedText? NameI18n, string? Description, LocalizedText? DescriptionI18n,
    int Position, bool Active, IReadOnlyList<TreeCategory> Categories);
public sealed record MenuTreeResponse(
    IReadOnlyList<TreeMenu> Menus, string DefaultLanguage, IReadOnlyList<string> SupportedLanguages);

// Scoped restaurant reads under /api/restaurants/{slug} (owner tenant, or staff for any tenant).
public static class RestaurantReadEndpoints
{
    public static void MapRestaurantReads(this RouteGroupBuilder scoped)
    {
        // GET /api/restaurants/{slug} — the restaurant plus a summary (with counts) of each menu.
        scoped.MapGet("/",
            async Task<Results<Ok<RestaurantOverview>, NotFound>> (
                string slug, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return TypedResults.NotFound();

            var menus = await db.Menus.AsNoTracking()
                .Where(m => m.RestaurantId == rest.Id)
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

            var summaries = menus.Select(m => new MenuSummary(
                m.Id, m.Name, m.Active, m.Position, m.UpdatedAt,
                categoryCounts.GetValueOrDefault(m.Id), dishCounts.GetValueOrDefault(m.Id))).ToList();

            return TypedResults.Ok(new RestaurantOverview(RestaurantDetail.From(rest), summaries));
        })
        .WithName("RestaurantOverview")
        .WithSummary("The restaurant plus a per-menu summary with counts (owner/staff).");

        // GET /api/restaurants/{slug}/tree — the full raw content tree for the builder.
        scoped.MapGet("/tree",
            async Task<Results<Ok<MenuTreeResponse>, NotFound>> (
                string slug, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return TypedResults.NotFound();

            var menus = await RawTreeAsync(db, rest.Id, ct);
            return TypedResults.Ok(new MenuTreeResponse(menus, rest.DefaultLanguage, rest.SupportedLanguages));
        })
        .WithName("MenuTree")
        .WithSummary("The full raw content tree — i18n intact, incl. inactive/unavailable (builder).");
    }

    // Three flat queries (no N+1) → nested tree, ordered by (position, created_at) at every level.
    // Nothing is filtered: the builder sees inactive menus and unavailable items too.
    private static async Task<List<TreeMenu>> RawTreeAsync(MenuDbContext db, Guid restaurantId, CancellationToken ct)
    {
        var menus = await db.Menus.AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.Position).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        if (menus.Count == 0) return [];

        var categories = await db.Categories.AsNoTracking()
            .Where(c => c.RestaurantId == restaurantId)
            .OrderBy(c => c.Position).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var items = await db.Items.AsNoTracking()
            .Where(i => i.RestaurantId == restaurantId)
            .OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToListAsync(ct);

        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var categoriesByMenu = categories.ToLookup(c => c.MenuId);

        return menus.Select(m => new TreeMenu(
            m.Id, m.Name, m.NameI18n, m.Description, m.DescriptionI18n, m.Position, m.Active,
            categoriesByMenu[m.Id].Select(c => new TreeCategory(
                c.Id, c.MenuId, c.Name, c.NameI18n, c.Description, c.DescriptionI18n, c.Position,
                itemsByCategory[c.Id].Select(i => new TreeItem(
                    i.Id, i.CategoryId, i.Name, i.NameI18n, i.Description, i.DescriptionI18n,
                    i.PriceCents, i.Currency, i.ImageUrl, i.Position, i.Available, i.Tags,
                    (i.Variants ?? []).Select(v => new TreeVariant(v.Label, v.LabelI18n, v.PriceCents)).ToList()))
                    .ToList()))
                .ToList()))
            .ToList();
    }
}
