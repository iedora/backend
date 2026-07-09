using System.Text.Json.Serialization;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// The JSON menu document an admin exports/edits/pastes back (ported from the contracts' import
// shape). Menus + categories carry only a name; items are the rich part. Optional fields are omitted
// on export (WhenWritingNull) so the document stays clean, and default on import.
public sealed record ImportVariant(
    string Label,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LocalizedText? LabelI18n,
    int PriceCents);

public sealed record ImportItem(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LocalizedText? NameI18n,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] LocalizedText? DescriptionI18n,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PriceCents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Currency,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Available,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? Tags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ImportVariant>? Variants);

public sealed record ImportCategory(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ImportItem>? Items);

public sealed record ImportMenu(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<ImportCategory>? Categories);

/// <summary>The staff "edit the whole menu as JSON" document — the menu tree only (identity +
/// languages are untouched). Both the GET export body and the PUT replace body.</summary>
public sealed record MenuImportDocument(IReadOnlyList<ImportMenu> Menus);

// Export a restaurant's live tree to the import shape, and replace it from a pasted document. Writes
// reuse the builder's normalization (BuilderText), so an import obeys the same limits as manual edits.
internal static class MenuImport
{
    private const int MaxMenus = 20;
    private const int MaxCategoriesPerMenu = 60;
    private const int MaxItemsPerCategory = 300;
    private const int MaxTotalItems = 2000; // one runaway paste can't stream thousands of rows into a tx

    // --- Export ---

    public static async Task<MenuImportDocument> ExportAsync(MenuDbContext db, Guid restaurantId, CancellationToken ct)
    {
        var menus = await db.Menus.AsNoTracking().Where(m => m.RestaurantId == restaurantId)
            .OrderBy(m => m.Position).ThenBy(m => m.CreatedAt).ToListAsync(ct);
        var categories = await db.Categories.AsNoTracking().Where(c => c.RestaurantId == restaurantId)
            .OrderBy(c => c.Position).ThenBy(c => c.CreatedAt).ToListAsync(ct);
        var items = await db.Items.AsNoTracking().Where(i => i.RestaurantId == restaurantId)
            .OrderBy(i => i.Position).ThenBy(i => i.CreatedAt).ToListAsync(ct);

        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var categoriesByMenu = categories.ToLookup(c => c.MenuId);

        var doc = menus.Select(m => new ImportMenu(m.Name,
            categoriesByMenu[m.Id].Select(c => new ImportCategory(c.Name,
                itemsByCategory[c.Id].Select(ToImportItem).ToList())).ToList())).ToList();
        return new MenuImportDocument(doc);
    }

    private static ImportItem ToImportItem(Item i)
    {
        static LocalizedText? NonEmpty(LocalizedText? m) => m is { Count: > 0 } ? m : null;

        var variants = i.Variants is { Count: > 0 }
            ? i.Variants.Select(v => new ImportVariant(v.Label, NonEmpty(v.LabelI18n), v.PriceCents)).ToList()
            : null;

        return new ImportItem(
            i.Name, NonEmpty(i.NameI18n),
            string.IsNullOrEmpty(i.Description) ? null : i.Description, NonEmpty(i.DescriptionI18n),
            // Variants drive the price when present; otherwise a non-zero price; a priceless item neither.
            variants is null && i.PriceCents > 0 ? i.PriceCents : null,
            i.Currency,
            i.Available ? null : false, // hidden items round-trip as available:false; visible ones omit it
            i.Tags is { Count: > 0 } ? i.Tags : null,
            variants);
    }

    // --- Replace ---

    /// <summary>Validate a document against the restaurant's languages + budget, then in one
    /// transaction drop every menu and write the new tree. Destructive (item ids are not preserved).</summary>
    public static async Task<ErrorOr<Success>> ReplaceAsync(
        MenuDbContext db, Restaurant rest, IReadOnlyList<ImportMenu> menus, TimeProvider clock, CancellationToken ct)
    {
        if (menus.Count is < 1 or > MaxMenus)
            return Error.Validation("menu.import_menu_count", $"An import must have 1 to {MaxMenus} menus.");
        var total = menus.Sum(m => (m.Categories ?? []).Sum(c => (c.Items ?? []).Count));
        if (total > MaxTotalItems)
            return Error.Validation("menu.import_too_many_items", $"Too many items: {total} (max {MaxTotalItems}).");

        var supported = new HashSet<string>(rest.SupportedLanguages) { rest.DefaultLanguage };

        // Normalize the whole tree up front — any error aborts before a single row is written.
        var built = new List<(string Name, List<(string Name, List<BuilderText.ItemFields> Items)> Categories)>();
        foreach (var menu in menus)
        {
            var menuName = BuilderText.Name(menu.Name, BuilderText.MaxShortName, "menu name");
            if (menuName.IsError) return menuName.Errors;
            if ((menu.Categories?.Count ?? 0) > MaxCategoriesPerMenu)
                return Error.Validation("menu.import_category_count", $"A menu may have at most {MaxCategoriesPerMenu} categories.");

            var cats = new List<(string, List<BuilderText.ItemFields>)>();
            foreach (var category in menu.Categories ?? [])
            {
                var catName = BuilderText.Name(category.Name, BuilderText.MaxShortName, "category name");
                if (catName.IsError) return catName.Errors;
                if ((category.Items?.Count ?? 0) > MaxItemsPerCategory)
                    return Error.Validation("menu.import_item_count", $"A category may have at most {MaxItemsPerCategory} items.");

                var built_items = new List<BuilderText.ItemFields>();
                foreach (var item in category.Items ?? [])
                {
                    if (UnsupportedTranslation(item, supported) is { } bad)
                        return Error.Validation("menu.import_unsupported_translation",
                            $"Translation language \"{bad}\" is not in the restaurant's supported languages.");

                    var fields = BuilderText.Item(
                        item.Name, item.NameI18n, item.Description, item.DescriptionI18n,
                        item.PriceCents ?? 0, item.Currency, item.Available,
                        item.Tags?.ToList(),
                        item.Variants?.Select(v => (v.Label, (Dictionary<string, string>?)v.LabelI18n, v.PriceCents)).ToList(),
                        rest.DefaultLanguage, rest.DefaultCurrency);
                    if (fields.IsError) return fields.Errors;
                    built_items.Add(fields.Value);
                }
                cats.Add((catName.Value, built_items));
            }
            built.Add((menuName.Value, cats));
        }

        var now = clock.GetUtcNow();
        // Drop + rewrite atomically. The Aspire Npgsql context uses a retrying execution strategy, so
        // a user-initiated transaction must run inside strategy.ExecuteAsync (it may re-run on a
        // transient failure — the pre-built tree makes that safe).
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear(); // drop anything a prior (retried) attempt left tracked, so we don't double-insert
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Snapshot per-dish view history keyed by name — the delete below cascades items → item_view,
            // which would wipe "top dishes". We re-point the counts to the new item ids by name afterward
            // (a dish that keeps its name keeps its history; a renamed/removed dish loses it). daily_view
            // is keyed by restaurant, not item, so overall-view analytics are untouched either way.
            var priorViews = await (
                from iv in db.ItemViews.AsNoTracking()
                join i in db.Items.AsNoTracking() on iv.ItemId equals i.Id
                where iv.RestaurantId == rest.Id
                select new { i.Name, iv.TenantId, iv.Day, iv.Count }).ToListAsync(ct);

            await db.Menus.Where(m => m.RestaurantId == rest.Id).ExecuteDeleteAsync(ct); // FK cascade drops categories + items

            var newItemIdByName = new Dictionary<string, Guid>(); // first item per name carries the history
            var mPos = 0;
            foreach (var (menuName, cats) in built)
            {
                var menu = new Menu { Id = Guid.CreateVersion7(), RestaurantId = rest.Id, Name = menuName, Position = mPos++, Active = true, CreatedAt = now, UpdatedAt = now };
                db.Menus.Add(menu);
                var cPos = 0;
                foreach (var (catName, catItems) in cats)
                {
                    var category = new Category { Id = Guid.CreateVersion7(), MenuId = menu.Id, RestaurantId = rest.Id, Name = catName, Position = cPos++, CreatedAt = now, UpdatedAt = now };
                    db.Categories.Add(category);
                    var iPos = 0;
                    foreach (var f in catItems)
                    {
                        var item = new Item
                        {
                            Id = Guid.CreateVersion7(), CategoryId = category.Id, RestaurantId = rest.Id,
                            Name = f.Name, NameI18n = f.NameI18n, Description = f.Description, DescriptionI18n = f.DescriptionI18n,
                            PriceCents = f.PriceCents, Currency = f.Currency, Available = f.Available, Tags = f.Tags,
                            Variants = f.Variants, Position = iPos++, CreatedAt = now, UpdatedAt = now,
                        };
                        db.Items.Add(item);
                        newItemIdByName.TryAdd(f.Name, item.Id);
                    }
                }
            }

            // Restore the preserved view counts onto the surviving dishes (aggregated per name+day, so
            // two old dishes sharing a name don't collide on item_view's (ItemId, Day) key).
            foreach (var g in priorViews.GroupBy(v => new { v.Name, v.TenantId, v.Day }))
                if (newItemIdByName.TryGetValue(g.Key.Name, out var newId))
                    db.ItemViews.Add(new ItemView { RestaurantId = rest.Id, TenantId = g.Key.TenantId, ItemId = newId, Day = g.Key.Day, Count = g.Sum(x => x.Count) });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
        return Result.Success;
    }

    // The first item-translation language not offered by the restaurant, or null if all are fine.
    private static string? UnsupportedTranslation(ImportItem item, HashSet<string> supported)
    {
        IEnumerable<string> keys = [];
        if (item.NameI18n is { } n) keys = keys.Concat(n.Keys);
        if (item.DescriptionI18n is { } d) keys = keys.Concat(d.Keys);
        return keys.FirstOrDefault(code => !supported.Contains(code));
    }
}
