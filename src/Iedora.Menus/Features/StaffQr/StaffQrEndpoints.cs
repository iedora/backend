using System.ComponentModel.DataAnnotations;
using ErrorOr;
using Framework.Web;
using Iedora.Data;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record QrCodeView(
    string Code, Guid? RestaurantId, string? RestaurantName, string? RestaurantSlug,
    string? Label, DateTimeOffset? BoundAt, DateTimeOffset CreatedAt);
public sealed record QrCodeListResponse(IReadOnlyList<QrCodeView> Codes);

// Create either one explicit code, or `Count` generated stickers (default 1, capped). Optionally
// pre-bound to a restaurant + labelled.
public sealed record CreateQrRequest(string? Code, int? Count, Guid? RestaurantId, string? Label);
public sealed record CreateQrResponse(int Inserted);

public sealed record BindQrRequest([property: Required] Guid RestaurantId);
public sealed record LabelQrRequest(string? Label);

// Cross-tenant QR sticker administration under /api/staff/qr-codes (staff = platform admin).
public static class StaffQrEndpoints
{
    public static void MapStaffQr(this RouteGroupBuilder staff)
    {
        // GET /api/staff/qr-codes — every sticker with its bound restaurant, newest binds first.
        staff.MapGet("/qr-codes", async (MenuDbContext db, CancellationToken ct) =>
        {
            var codes = await (
                from q in db.QrCodes.AsNoTracking()
                join r in db.Restaurants.AsNoTracking() on q.RestaurantId equals r.Id into rj
                from r in rj.DefaultIfEmpty()
                orderby q.BoundAt == null, q.BoundAt descending, q.CreatedAt descending
                select new QrCodeView(
                    q.Code, q.RestaurantId, r != null ? r.Name : null, r != null ? r.Slug : null,
                    q.Label, q.BoundAt, q.CreatedAt)).ToListAsync(ct);
            return TypedResults.Ok(new QrCodeListResponse(codes));
        })
        .WithName("ListQrCodes").WithSummary("List all QR stickers with their bound restaurant (staff).");

        // POST /api/staff/qr-codes — mint an explicit code or a batch; optionally pre-bind + label.
        staff.MapPost("/qr-codes", async (
                CreateQrRequest req, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var batch = ResolveBatch(req.Code, req.Count);
            if (batch.IsError) return ProblemResults.From(batch.Errors);

            if (req.RestaurantId is { } rid && !await db.Restaurants.AnyAsync(r => r.Id == rid, ct))
                return ProblemResults.From(MenuErrors.UnknownRestaurant);

            var existing = await db.QrCodes.Where(q => batch.Value.Contains(q.Code)).Select(q => q.Code).ToListAsync(ct);
            var fresh = batch.Value.Except(existing).ToList(); // ON CONFLICT DO NOTHING (idempotent)

            var now = clock.GetUtcNow();
            var label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();
            foreach (var code in fresh)
                db.QrCodes.Add(new QrCode
                {
                    Code = code, RestaurantId = req.RestaurantId, Label = label,
                    BoundAt = req.RestaurantId is null ? null : now, CreatedAt = now,
                });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new CreateQrResponse(fresh.Count));
        })
        .WithName("CreateQrCodes").WithSummary("Create QR stickers (staff).")
        .Produces<CreateQrResponse>(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest);

        // POST /api/staff/qr-codes/{code}/bind — point a sticker at a restaurant.
        staff.MapPost("/qr-codes/{code}/bind", async (
                string code, BindQrRequest req, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            if (!await db.Restaurants.AnyAsync(r => r.Id == req.RestaurantId, ct))
                return ProblemResults.From(MenuErrors.UnknownRestaurant);

            var qr = await db.QrCodes.FirstOrDefaultAsync(q => q.Code == QrCodes.Normalize(code), ct);
            if (qr is null) return Results.NotFound();

            qr.RestaurantId = req.RestaurantId;
            qr.BoundAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("BindQrCode").WithSummary("Bind a QR sticker to a restaurant (staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/staff/qr-codes/{code}/unbind — free a sticker for reuse.
        staff.MapPost("/qr-codes/{code}/unbind", async (string code, MenuDbContext db, CancellationToken ct) =>
        {
            var qr = await db.QrCodes.FirstOrDefaultAsync(q => q.Code == QrCodes.Normalize(code), ct);
            if (qr is null) return Results.NotFound();

            qr.RestaurantId = null;
            qr.BoundAt = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("UnbindQrCode").WithSummary("Unbind a QR sticker (staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        // PATCH /api/staff/qr-codes/{code} — set/clear the sticker's label.
        staff.MapPatch("/qr-codes/{code}", async (
                string code, LabelQrRequest req, MenuDbContext db, CancellationToken ct) =>
        {
            var qr = await db.QrCodes.FirstOrDefaultAsync(q => q.Code == QrCodes.Normalize(code), ct);
            if (qr is null) return Results.NotFound();

            qr.Label = string.IsNullOrWhiteSpace(req.Label) ? null : req.Label.Trim();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("LabelQrCode").WithSummary("Set a QR sticker's label (staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        // DELETE /api/staff/qr-codes/{code}
        staff.MapDelete("/qr-codes/{code}", async (string code, MenuDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.QrCodes.Where(q => q.Code == QrCodes.Normalize(code)).ExecuteDeleteAsync(ct);
            return deleted > 0 ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteQrCode").WithSummary("Delete a QR sticker (staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    // An explicit code (normalized + validated) wins; otherwise `count` generated ones (1..MaxBulk).
    private static ErrorOr<List<string>> ResolveBatch(string? code, int? count)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            var normalized = QrCodes.Normalize(code);
            return QrCodes.IsValid(normalized) ? new List<string> { normalized } : MenuErrors.InvalidQrCode;
        }
        var n = Math.Clamp(count ?? 1, 1, QrCodes.MaxBulk);
        return Enumerable.Range(0, n).Select(_ => QrCodes.Generate()).Distinct().ToList();
    }
}
