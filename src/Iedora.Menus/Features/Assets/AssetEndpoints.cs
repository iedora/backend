using System.Security.Claims;
using Framework.Storage;
using Framework.Web;
using Iedora.Identity.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

/// <summary>Attach (or clear) an image already uploaded to the Media service. The browser uploads to
/// <c>POST /media/images</c>, gets a URL back, then PUTs it here — Menu validates the URL is one of
/// ours and scoped to this restaurant's tenant, persists it to the right column, and deletes the
/// object it replaced. A null URL clears the asset.</summary>
public sealed record AttachAssetRequest(string? Url);

// Asset attach/clear under /api/restaurants/{slug} (owner/staff, scoped, synchronous).
public static class AssetEndpoints
{
    public static void MapAssets(this RouteGroupBuilder scoped)
    {
        MapRestaurantAsset(scoped, "logo", r => r.LogoUrl, (r, v) => r.LogoUrl = v);
        MapRestaurantAsset(scoped, "banner", r => r.BannerUrl, (r, v) => r.BannerUrl = v);

        // PUT /api/restaurants/{slug}/items/{itemId}/image — attach/clear a dish photo.
        scoped.MapPut("/items/{itemId:guid}/image", async (
                string slug, Guid itemId, AttachAssetRequest req, ClaimsPrincipal user,
                MenuDbContext db, IObjectStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadTrackedAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();
            var item = await db.Items.FirstOrDefaultAsync(i => i.Id == itemId && i.RestaurantId == rest.Id, ct);
            if (item is null) return Results.NotFound();

            var url = Validate(req.Url, store, rest.TenantId, user);
            if (url.IsError) return ProblemResults.From(url.Errors);

            var previous = item.ImageUrl;
            item.ImageUrl = url.Value;
            item.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            DeletePrevious(store, previous);
            return Results.NoContent();
        })
        .WithName("AttachItemPhoto").WithSummary("Attach or clear a dish photo (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    // PUT /api/restaurants/{slug}/{name} — attach/clear a restaurant-level asset (logo/banner).
    private static void MapRestaurantAsset(
        RouteGroupBuilder scoped, string name, Func<Restaurant, string?> get, Action<Restaurant, string?> set)
    {
        scoped.MapPut($"/{name}", async (
                string slug, AttachAssetRequest req, ClaimsPrincipal user,
                MenuDbContext db, IObjectStore store, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadTrackedAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var url = Validate(req.Url, store, rest.TenantId, user);
            if (url.IsError) return ProblemResults.From(url.Errors);

            var previous = get(rest);
            set(rest, url.Value);
            rest.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            DeletePrevious(store, previous);
            return Results.NoContent();
        })
        .WithName($"AttachRestaurant{name}").WithSummary($"Attach or clear a restaurant's {name} (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);
    }

    // A null/blank URL clears the asset. Otherwise it must be a Media URL scoped to this restaurant's
    // tenant (t/{tenantId}/…) — so a caller can't attach a foreign or another tenant's object. Platform
    // staff, who legitimately act across tenants, only need it to be one of ours.
    private static ErrorOr.ErrorOr<string?> Validate(
        string? url, IObjectStore store, Guid tenantId, ClaimsPrincipal user)
    {
        if (string.IsNullOrWhiteSpace(url)) return (string?)null;
        if (store.KeyFromPublicUrl(url) is not { } key) return MenuErrors.ForeignAssetUrl;
        if (!user.IsInRole(Roles.Admin) && !key.StartsWith($"t/{tenantId}/", StringComparison.Ordinal))
            return MenuErrors.ForeignAssetUrl;
        return url;
    }

    // Drop the object behind a previously stored URL — ignoring foreign/absent URLs.
    private static void DeletePrevious(IObjectStore store, string? url)
    {
        if (url is not null && store.KeyFromPublicUrl(url) is { } key) store.Delete(key);
    }
}
