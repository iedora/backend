using Iedora.Data;

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
        // Call AddValidation() HERE (not in the host) so the .NET 10 validation source generator runs
        // in this assembly and discovers this module's endpoints' request DTOs.
        builder.Services.AddValidation();
        return builder;
    }

    /// <summary>Map the module's slices. The guest surface lives under <c>/public</c> (unauthenticated);
    /// the owner/staff surfaces under <c>/api</c> land in later slices.</summary>
    public static IEndpointRouteBuilder MapMenuModule(this IEndpointRouteBuilder app)
    {
        var pub = app.MapGroup("/public").AllowAnonymous();
        pub.MapPublicMenu(); // GET /public/r/{slug}
        return app;
    }
}
