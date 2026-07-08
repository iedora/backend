using Framework.Storage;
using Iedora.Identity.Contracts;

namespace Iedora.Menus;

/// <summary>
/// The <b>Menu</b> module: restaurants and their content hierarchy (menus → categories → items),
/// plus the guest-facing public render. Owns the <c>menu</c> schema (<see cref="MenuDbContext"/>).
/// The host composes it via <see cref="AddMenuModule"/> + <see cref="MapMenuModule"/>.
/// </summary>
public static class MenuModule
{
    public static IHostApplicationBuilder AddMenuModule(this IHostApplicationBuilder builder)
    {
        builder.AddMenuDb();
        // The Media service owns uploads/serving; Menu only needs the store to validate an attached
        // URL is ours and to delete the object it replaces (idempotent — Media registers it too).
        builder.Services.AddLocalDiskStorage(builder.Configuration);
        // Call AddValidation() HERE (not in the host) so the .NET 10 validation source generator runs
        // in this assembly and discovers this module's endpoints' request DTOs.
        builder.Services.AddValidation();
        return builder;
    }

    /// <summary>Map the module's slices: the unauthenticated guest surface under <c>/public</c>, and
    /// the authenticated owner/staff surface under <c>/api</c> (scoped restaurant access is enforced
    /// per-request by <see cref="RestaurantAccess"/>).</summary>
    public static IEndpointRouteBuilder MapMenuModule(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/public").AllowAnonymous();
        pub.MapPublicMenu(); // GET /public/r/{slug}
        pub.MapPublicQr();   // GET /public/qr/{code}
        pub.MapTracking();   // GET /public/track/{slug}, POST /public/track/{slug}/session

        var api = app.MapGroup("/api").RequireAuthorization();
        api.MapGroup("/dashboard").MapDashboard(); // tenant-scoped analytics (reads over the caller's tenant)

        var restaurant = api.MapGroup("/restaurants/{slug}");
        restaurant.MapRestaurantReads();  // GET /api/restaurants/{slug}[/tree]
        restaurant.MapRestaurantWrites(); // PATCH identity, rename slug, complete-onboarding, DELETE
        restaurant.MapMenuBuilder();      // menu create/update/delete
        restaurant.MapCategoryBuilder();  // category create/update/delete + reorder
        restaurant.MapItemBuilder();      // item create/update/delete + reorder
        restaurant.MapAssets();           // attach/clear logo, banner, item photo (URLs from /media/images)

        // Cross-tenant staff surface (platform admin role).
        var staff = api.MapGroup("/staff").RequireAuthorization(Policies.Admin);
        staff.MapStaffConsole(); // /api/staff/{overview,directory,alerts,restaurants/{id}}
        staff.MapStaffQr();      // /api/staff/qr-codes …
        return app;
    }
}
