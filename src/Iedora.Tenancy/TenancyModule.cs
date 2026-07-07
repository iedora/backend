using Iedora.Kernel;
using Iedora.Contracts;

namespace Iedora.Tenancy;

/// <summary>
/// The <b>Tenancy</b> module: tenants + memberships. Owns the <c>tenancy</c> schema
/// (<see cref="TenancyDbContext"/>) and exposes <see cref="ITenancyApi"/> as its only
/// cross-module surface (used by the Identity module's login).
/// </summary>
public static class TenancyModule
{
    public static IHostApplicationBuilder AddTenancyModule(this IHostApplicationBuilder builder)
    {
        builder.AddTenancyDb();
        builder.Services.AddScoped<ITenancyApi, TenancyApi>();
        // Call AddValidation() HERE (not in the host) so the .NET 10 validation source generator runs
        // in this assembly and discovers this module's endpoints' [Required]/etc. request DTOs.
        builder.Services.AddValidation();
        return builder;
    }

    /// <summary>Map the module's vertical slices under its own prefix (<c>/tenancy</c>).</summary>
    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tenancy");
        group.MapTenants();
        group.MapTenantAdmin();
        group.MapCommandStatus<TenancyDbContext>(); // GET /tenancy/commands/{id}
        return app;
    }
}
