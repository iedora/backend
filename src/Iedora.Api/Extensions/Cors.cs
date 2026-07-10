namespace Iedora.Api;

/// <summary>CORS for browser SPA consumers — the admin dashboard and (later) the front-office. Allowed
/// origins come from config (<c>Cors:AllowedOrigins</c>); credentials are allowed so the API's refresh
/// cookie flows on the cross-origin <c>/auth/refresh</c> call. With no configured origins the policy is
/// a no-op (same-origin / server-to-server deployments need nothing).</summary>
public static class CorsExtensions
{
    public const string PolicyName = "spa";

    public static WebApplicationBuilder AddIedoraCors(this WebApplicationBuilder builder)
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        builder.Services.AddCors(options => options.AddPolicy(PolicyName, policy =>
        {
            if (origins.Length > 0)
                policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        return builder;
    }
}
