using Framework.Outbox;
using Iedora.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Iedora.Tenancy;

public static class TenancyMessaging
{
    /// <summary>
    /// Register the Tenancy module's async-write dispatch into a host (the generic worker): its
    /// DbContext, the outbox dispatcher for it, and its command handlers. Self-contained — the
    /// worker just calls this, so it never references the API web project.
    /// </summary>
    public static IHostApplicationBuilder AddTenancyMessaging(this IHostApplicationBuilder builder)
    {
        builder.AddTenancyDb();
        builder.Services.AddTenancyHandlers();
        return builder;
    }

    /// <summary>Register the Tenancy outbox dispatcher + its command handlers onto an existing
    /// service collection (the DbContext is registered separately). Lets the API's integration-test
    /// host drive the pipeline in-process, and keeps the internal handlers internal.</summary>
    public static IServiceCollection AddTenancyHandlers(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddOutbox<TenancyDbContext>();
        services.AddScoped<IOutboxHandler, CreateTenantHandler>();
        return services;
    }
}
