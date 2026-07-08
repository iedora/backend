using Framework.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

// Staff "edit the whole menu as JSON" for an existing restaurant (platform admin), under
// /api/staff/restaurants/{id}/menus. GET serializes the live tree into the import shape; PUT replaces
// every menu from a pasted document. The restaurant's identity + languages are untouched.
public static class StaffMenuImportEndpoints
{
    public static void MapStaffMenuImport(this RouteGroupBuilder staff)
    {
        // GET /api/staff/restaurants/{id}/menus — the live menu tree as an editable JSON document.
        staff.MapGet("/restaurants/{id:guid}/menus",
            async Task<Results<Ok<MenuImportDocument>, NotFound>> (
                Guid id, MenuDbContext db, CancellationToken ct) =>
        {
            if (!await db.Restaurants.AnyAsync(r => r.Id == id, ct)) return TypedResults.NotFound();
            return TypedResults.Ok(await MenuImport.ExportAsync(db, id, ct));
        })
        .WithName("StaffExportMenus").WithSummary("Export a restaurant's menu tree as JSON (staff).")
        .ProducesProblem(StatusCodes.Status404NotFound);

        // PUT /api/staff/restaurants/{id}/menus — replace every menu from a pasted document.
        staff.MapPut("/restaurants/{id:guid}/menus", async (
                Guid id, MenuImportDocument body, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await db.Restaurants.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rest is null) return Results.NotFound();

            var result = await MenuImport.ReplaceAsync(db, rest, body.Menus ?? [], clock, ct);
            return result.IsError ? ProblemResults.From(result.Errors) : Results.NoContent();
        })
        .WithName("StaffReplaceMenus").WithSummary("Replace a restaurant's menus from a JSON document (staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }
}
