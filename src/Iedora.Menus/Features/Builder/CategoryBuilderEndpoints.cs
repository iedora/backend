using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Framework.Web;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record CreateCategoryRequest([property: Required, StringLength(80, MinimumLength = 1)] string Name);
public sealed record UpdateCategoryRequest(
    [property: Required, StringLength(80, MinimumLength = 1)] string Name,
    [property: StringLength(1000)] string? Description,
    Dictionary<string, string>? NameI18n,
    Dictionary<string, string>? DescriptionI18n);
public sealed record ReorderRequest([property: Required] IReadOnlyList<Guid> OrderedIds);

// Category writes under /api/restaurants/{slug} (owner/staff, scoped, synchronous). A category is
// created under a menu; updates/deletes/reorders are restaurant-scoped so a forged id → 404.
public static class CategoryBuilderEndpoints
{
    public static void MapCategoryBuilder(this RouteGroupBuilder scoped)
    {
        scoped.MapPost("/menus/{menuId:guid}/categories", async (
                string slug, Guid menuId, CreateCategoryRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var name = BuilderText.Name(req.Name, BuilderText.MaxShortName, "name");
            if (name.IsError) return ProblemResults.From(name.Errors);

            // The parent menu must belong to this restaurant.
            if (!await db.Menus.AnyAsync(m => m.Id == menuId && m.RestaurantId == rest.Id, ct))
                return Results.NotFound();

            var now = clock.GetUtcNow();
            var nextPosition = (await db.Categories.Where(c => c.MenuId == menuId)
                .Select(c => (int?)c.Position).MaxAsync(ct) ?? -1) + 1;
            var category = new Category
            {
                Id = Guid.CreateVersion7(), MenuId = menuId, RestaurantId = rest.Id, Name = name.Value,
                Position = nextPosition, CreatedAt = now, UpdatedAt = now,
            };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            return Results.Created((string?)null, new CreatedId(category.Id));
        })
        .WithName("CreateCategory").WithSummary("Create a category under a menu (owner/staff).")
        .Produces<CreatedId>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapPatch("/categories/{categoryId:guid}", async (
                string slug, Guid categoryId, UpdateCategoryRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var text = BuilderText.Text(req.Name, req.Description, req.NameI18n, req.DescriptionI18n, rest.DefaultLanguage);
            if (text.IsError) return ProblemResults.From(text.Errors);

            var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId && c.RestaurantId == rest.Id, ct);
            if (category is null) return Results.NotFound();

            (category.Name, category.Description, category.NameI18n, category.DescriptionI18n) =
                (text.Value.Name, text.Value.Description, text.Value.NameI18n, text.Value.DescriptionI18n);
            category.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("UpdateCategory").WithSummary("Update a category (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapDelete("/categories/{categoryId:guid}", async (
                string slug, Guid categoryId, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var deleted = await db.Categories
                .Where(c => c.Id == categoryId && c.RestaurantId == rest.Id)
                .ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteCategory").WithSummary("Delete a category and its items (owner/staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        // Reorder the categories under a menu. orderedIds must list every category under the menu
        // exactly once (a stale/partial/foreign list is rejected, never half-applied).
        scoped.MapPut("/menus/{menuId:guid}/category-order", async (
                string slug, Guid menuId, ReorderRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();
            if (!await db.Menus.AnyAsync(m => m.Id == menuId && m.RestaurantId == rest.Id, ct))
                return Results.NotFound();

            var categories = await db.Categories.Where(c => c.MenuId == menuId && c.RestaurantId == rest.Id).ToListAsync(ct);
            if (!IsPermutation(req.OrderedIds, categories.Select(c => c.Id)))
                return ProblemResults.From(MenuErrors.InvalidReorder);

            var byId = categories.ToDictionary(c => c.Id);
            var now = clock.GetUtcNow();
            for (var i = 0; i < req.OrderedIds.Count; i++)
            {
                byId[req.OrderedIds[i]].Position = i;
                byId[req.OrderedIds[i]].UpdatedAt = now;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderCategories").WithSummary("Reorder the categories under a menu (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    // True iff ordered names every id in existing exactly once (same set, same count, no duplicates).
    internal static bool IsPermutation(IReadOnlyList<Guid> ordered, IEnumerable<Guid> existing)
    {
        var existingSet = existing.ToHashSet();
        return ordered.Count == existingSet.Count && ordered.ToHashSet().SetEquals(existingSet);
    }
}
