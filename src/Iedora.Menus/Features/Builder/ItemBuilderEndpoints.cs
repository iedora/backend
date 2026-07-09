using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ErrorOr;
using Framework.Web;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record VariantInput(
    [property: Required, StringLength(120, MinimumLength = 1)] string Label,
    Dictionary<string, string>? LabelI18n,
    [property: Range(0, 100_000_00)] int PriceCents);

public sealed record ItemWriteRequest(
    [property: Required, StringLength(120, MinimumLength = 1)] string Name,
    Dictionary<string, string>? NameI18n,
    [property: StringLength(1000)] string? Description,
    Dictionary<string, string>? DescriptionI18n,
    [property: Range(0, 100_000_00)] int PriceCents,
    string? Currency,
    bool? Available,
    [property: MaxLength(20)] List<string>? Tags,
    [property: MaxLength(20)] List<VariantInput>? Variants);

// Item writes under /api/restaurants/{slug} (owner/staff, scoped, synchronous). An item is created
// under a category; updates/deletes/reorders are restaurant-scoped so a forged id → 404.
public static class ItemBuilderEndpoints
{
    public static void MapItemBuilder(this RouteGroupBuilder scoped)
    {
        scoped.MapPost("/categories/{categoryId:guid}/items", async (
                string slug, Guid categoryId, ItemWriteRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var fields = Normalize(req, rest);
            if (fields.IsError) return ProblemResults.From(fields.Errors);

            if (!await db.Categories.AnyAsync(c => c.Id == categoryId && c.RestaurantId == rest.Id, ct))
                return Results.NotFound();

            var now = clock.GetUtcNow();
            var nextPosition = (await db.Items.Where(i => i.CategoryId == categoryId)
                .Select(i => (int?)i.Position).MaxAsync(ct) ?? -1) + 1;
            var f = fields.Value;
            var item = new Item
            {
                Id = Guid.CreateVersion7(), CategoryId = categoryId, RestaurantId = rest.Id,
                Name = f.Name, NameI18n = f.NameI18n, Description = f.Description, DescriptionI18n = f.DescriptionI18n,
                PriceCents = f.PriceCents, Currency = f.Currency, Available = f.Available, Tags = f.Tags,
                Variants = f.Variants, Position = nextPosition, CreatedAt = now, UpdatedAt = now,
            };
            db.Items.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created((string?)null, new CreatedId(item.Id));
        })
        .WithName("CreateItem").WithSummary("Create an item under a category (owner/staff).")
        .Produces<CreatedId>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapPatch("/items/{itemId:guid}", async (
                string slug, Guid itemId, ItemWriteRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var fields = Normalize(req, rest);
            if (fields.IsError) return ProblemResults.From(fields.Errors);

            var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.RestaurantId == rest.Id, ct);
            if (item is null) return Results.NotFound();

            var f = fields.Value;
            (item.Name, item.NameI18n, item.Description, item.DescriptionI18n) = (f.Name, f.NameI18n, f.Description, f.DescriptionI18n);
            (item.PriceCents, item.Currency, item.Available, item.Tags) = (f.PriceCents, f.Currency, f.Available, f.Tags);
            // Variants absent from the request ⇒ leave stored variants; present (even empty) ⇒ replace.
            if (req.Variants is not null) item.Variants = f.Variants;
            item.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("UpdateItem").WithSummary("Update an item (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapDelete("/items/{itemId:guid}", async (
                string slug, Guid itemId, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var deleted = await db.Items.Where(i => i.Id == itemId && i.RestaurantId == rest.Id).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteItem").WithSummary("Delete an item (owner/staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        // Reorder the items under a category. orderedIds must list every item exactly once.
        scoped.MapPut("/categories/{categoryId:guid}/item-order", async (
                string slug, Guid categoryId, ReorderRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();
            if (!await db.Categories.AnyAsync(c => c.Id == categoryId && c.RestaurantId == rest.Id, ct))
                return Results.NotFound();

            var items = await db.Items.Where(i => i.CategoryId == categoryId && i.RestaurantId == rest.Id).ToListAsync(ct);
            if (!Reorder.Apply(items, req.OrderedIds, clock.GetUtcNow()))
                return ProblemResults.From(MenuErrors.InvalidReorder);

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderItems").WithSummary("Reorder the items under a category (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static ErrorOr<BuilderText.ItemFields> Normalize(ItemWriteRequest req, Restaurant rest) =>
        BuilderText.Item(
            req.Name, req.NameI18n, req.Description, req.DescriptionI18n, req.PriceCents, req.Currency,
            req.Available, req.Tags,
            req.Variants?.Select(v => (v.Label, v.LabelI18n, v.PriceCents)).ToList(),
            rest.DefaultLanguage, rest.DefaultCurrency);
}
