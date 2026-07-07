using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

/// <summary>The slug a scanned QR sticker points at.</summary>
public sealed record QrTarget(string Slug);

// GET /public/qr/{code} — the sticker-scan hot path: resolve a code to its bound restaurant's slug.
// Unauthenticated. Unknown, unbound, and malformed codes all 404 (indistinguishable on purpose).
public static class PublicQrEndpoint
{
    public static void MapPublicQr(this RouteGroupBuilder group) =>
        group.MapGet("/qr/{code}",
            async Task<Results<Ok<QrTarget>, NotFound>> (string code, MenuDbContext db, CancellationToken ct) =>
        {
            var normalized = QrCodes.Normalize(code);
            if (!QrCodes.IsValid(normalized)) return TypedResults.NotFound();

            var slug = await db.QrCodes.AsNoTracking()
                .Where(q => q.Code == normalized && q.RestaurantId != null)
                .Join(db.Restaurants, q => q.RestaurantId, r => r.Id, (_, r) => r.Slug)
                .FirstOrDefaultAsync(ct);

            return slug is null ? TypedResults.NotFound() : TypedResults.Ok(new QrTarget(slug));
        })
        .WithName("ResolveQr")
        .WithSummary("Resolve a QR sticker code to its restaurant slug.")
        .Produces<QrTarget>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound);
}
