using Iedora.Api.Features.Tenants;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iedora.Api.Tenancy;

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
        return builder;
    }

    /// <summary>Map the module's vertical-slice endpoints under the group (/auth).</summary>
    public static RouteGroupBuilder MapTenancyModule(this RouteGroupBuilder group)
    {
        group.MapTenants();
        return group;
    }
}
