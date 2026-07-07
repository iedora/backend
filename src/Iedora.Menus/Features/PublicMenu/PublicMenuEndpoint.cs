using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// The localized public read model — what the guest-facing React app renders. Unavailable items and
// inactive menus are already dropped; i18n maps are collapsed to the negotiated language.
public sealed record PublicVariant(string Label, int PriceCents);
public sealed record PublicItem(
    Guid Id, string Name, string? Description, int PriceCents, string Currency,
    string? ImageUrl, IReadOnlyList<string> Tags, IReadOnlyList<PublicVariant> Variants);
public sealed record PublicCategory(Guid Id, string Name, string? Description, IReadOnlyList<PublicItem> Items);
public sealed record PublicMenu(Guid Id, string Name, string? Description, IReadOnlyList<PublicCategory> Categories);
public sealed record PublicRestaurant(
    string Name, string Slug, string? Description, string? LogoUrl, string? BannerUrl, Theme? Theme);
public sealed record PublicPayload(
    PublicRestaurant Restaurant, IReadOnlyList<PublicMenu> Menus,
    string DefaultLanguage, IReadOnlyList<string> SupportedLanguages, string CurrentLanguage);

// GET /public/r/{slug} — unauthenticated, slug-addressed render of one restaurant's active menus in
// the negotiated language. The heart of the guest experience.
public static class PublicMenuEndpoint
{
    public static void MapPublicMenu(this RouteGroupBuilder group) =>
        group.MapGet("/r/{slug}",
            async Task<Results<Ok<PublicPayload>, NotFound>> (
                string slug, MenuDbContext db, HttpContext http, CancellationToken ct) =>
        {
            var rest = await db.Restaurants.AsNoTracking().FirstOrDefaultAsync(r => r.Slug == slug, ct);
            if (rest is null) return TypedResults.NotFound();

            var lang = Localization.PickLanguage(
                http.Request.Query["lang"].ToString(),
                http.Request.Headers.AcceptLanguage.ToString(),
                rest.SupportedLanguages, rest.DefaultLanguage);

            var menus = await LocalizedTreeAsync(db, rest.Id, lang, ct);

            return TypedResults.Ok(new PublicPayload(
                new PublicRestaurant(
                    rest.Name, rest.Slug,
                    NullIfEmpty(Localization.Pick(rest.Description, rest.DescriptionI18n, lang)),
                    rest.LogoUrl, rest.BannerUrl, rest.Theme),
                menus, rest.DefaultLanguage, rest.SupportedLanguages, lang));
        })
        .WithName("PublicMenu")
        .WithSummary("Render a restaurant's active menus for guests (localized).")
        .Produces<PublicPayload>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);

    // Three flat queries (no N+1), assembled + localized in memory: active menus, their categories,
    // and the AVAILABLE items — each ordered by position. Guests never see inactive/unavailable rows.
    private static async Task<List<PublicMenu>> LocalizedTreeAsync(
        MenuDbContext db, Guid restaurantId, string lang, CancellationToken ct)
    {
        var menus = await db.Menus.AsNoTracking()
            .Where(m => m.RestaurantId == restaurantId && m.Active)
            .OrderBy(m => m.Position).ToListAsync(ct);
        if (menus.Count == 0) return [];

        var menuIds = menus.Select(m => m.Id).ToList();
        var categories = await db.Categories.AsNoTracking()
            .Where(c => menuIds.Contains(c.MenuId))
            .OrderBy(c => c.Position).ToListAsync(ct);

        var categoryIds = categories.Select(c => c.Id).ToList();
        var items = await db.Items.AsNoTracking()
            .Where(i => categoryIds.Contains(i.CategoryId) && i.Available)
            .OrderBy(i => i.Position).ToListAsync(ct);

        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var categoriesByMenu = categories.ToLookup(c => c.MenuId);

        return menus.Select(m => new PublicMenu(
            m.Id,
            Localization.Pick(m.Name, m.NameI18n, lang),
            NullIfEmpty(Localization.Pick(m.Description, m.DescriptionI18n, lang)),
            categoriesByMenu[m.Id].Select(c => new PublicCategory(
                c.Id,
                Localization.Pick(c.Name, c.NameI18n, lang),
                NullIfEmpty(Localization.Pick(c.Description, c.DescriptionI18n, lang)),
                itemsByCategory[c.Id].Select(i => new PublicItem(
                    i.Id,
                    Localization.Pick(i.Name, i.NameI18n, lang),
                    NullIfEmpty(Localization.Pick(i.Description, i.DescriptionI18n, lang)),
                    i.PriceCents, i.Currency, i.ImageUrl, i.Tags,
                    (i.Variants ?? []).Select(v => new PublicVariant(
                        Localization.Pick(v.Label, v.LabelI18n, lang), v.PriceCents)).ToList()))
                    .ToList()))
                .ToList()))
            .ToList();
    }

    private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;
}
