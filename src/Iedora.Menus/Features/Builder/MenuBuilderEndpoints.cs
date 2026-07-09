using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Framework.Web;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record CreateMenuRequest([property: Required, StringLength(BuilderText.MaxShortName, MinimumLength = 1)] string Name);
public sealed record UpdateMenuRequest(
    [property: Required, StringLength(BuilderText.MaxShortName, MinimumLength = 1)] string Name,
    [property: StringLength(BuilderText.MaxDescription)] string? Description,
    Dictionary<string, string>? NameI18n,
    Dictionary<string, string>? DescriptionI18n,
    bool Active);

/// <summary>The id of a freshly created builder resource.</summary>
public sealed record CreatedId(Guid Id);

// Menu writes under /api/restaurants/{slug} (owner/staff, scoped). SYNCHRONOUS CRUD: the builder is a
// live editor with no side effects, so writes save + return immediately rather than going through the
// async command pipeline. Every mutation is restaurant-scoped, so a forged/cross-tenant id → 404.
public static class MenuBuilderEndpoints
{
    public static void MapMenuBuilder(this RouteGroupBuilder scoped)
    {
        scoped.MapPost("/menus", async (
                string slug, CreateMenuRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var name = BuilderText.Name(req.Name, BuilderText.MaxShortName, "name");
            if (name.IsError) return ProblemResults.From(name.Errors);

            var now = clock.GetUtcNow();
            var nextPosition = (await db.Menus.Where(m => m.RestaurantId == rest.Id)
                .Select(m => (int?)m.Position).MaxAsync(ct) ?? -1) + 1;
            var menu = new Menu
            {
                Id = Guid.CreateVersion7(), RestaurantId = rest.Id, Name = name.Value,
                Position = nextPosition, Active = true, CreatedAt = now, UpdatedAt = now,
            };
            db.Menus.Add(menu);
            await db.SaveChangesAsync(ct);
            return Results.Created((string?)null, new CreatedId(menu.Id));
        })
        .WithName("CreateMenu").WithSummary("Create a menu (owner/staff).")
        .Produces<CreatedId>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapPatch("/menus/{menuId:guid}", async (
                string slug, Guid menuId, UpdateMenuRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var text = BuilderText.Text(req.Name, req.Description, req.NameI18n, req.DescriptionI18n, rest.DefaultLanguage);
            if (text.IsError) return ProblemResults.From(text.Errors);

            var menu = await db.Menus.FirstOrDefaultAsync(m => m.Id == menuId && m.RestaurantId == rest.Id, ct);
            if (menu is null) return Results.NotFound();

            (menu.Name, menu.Description, menu.NameI18n, menu.DescriptionI18n) =
                (text.Value.Name, text.Value.Description, text.Value.NameI18n, text.Value.DescriptionI18n);
            menu.Active = req.Active;
            menu.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("UpdateMenu").WithSummary("Update a menu (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        scoped.MapDelete("/menus/{menuId:guid}", async (
                string slug, Guid menuId, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            // Restaurant-scoped delete; cascade drops categories + items. 0 rows ⇒ unknown/foreign.
            var deleted = await db.Menus
                .Where(m => m.Id == menuId && m.RestaurantId == rest.Id)
                .ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteMenu").WithSummary("Delete a menu and its contents (owner/staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }
}
