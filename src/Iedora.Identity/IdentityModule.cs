using Framework.Web;
using Iedora.Identity.Contracts;
using Iedora.Tenancy.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

/// <summary>
/// The <b>Identity</b> module: authentication + accounts — users, roles, refresh sessions,
/// password lifecycle, and the ES256 JWT/JWKS issuer. Owns the <c>identity</c> schema
/// (<see cref="IdentityDbContext"/>). The host composes it via <see cref="AddIdentityModule"/> +
/// <see cref="MapIdentityModule"/>; nothing outside the module reaches into these registrations.
/// </summary>
public static class IdentityModule
{
    public static IHostApplicationBuilder AddIdentityModule(this IHostApplicationBuilder builder)
    {
        // Telemetry (auth.login span, session/signup metrics) rides ServiceDefaults' AddSource/
        // AddMeter("Iedora.*") — no per-module registration needed, and it reaches the Worker (where
        // the outbox handlers emit) too, which a registration only in this method would miss.
        builder.AddIdentityDb();

        // Call AddValidation() HERE (not in the host) so the .NET 10 validation source generator runs
        // in this assembly and discovers this module's endpoints' [Required]/etc. request DTOs.
        builder.Services.AddValidation();

        // The module's cross-module surface (other modules resolve users by id through this).
        builder.Services.AddScoped<IIdentityApi, IdentityApi>();

        // Full ASP.NET Core Identity over EF Core (PasswordHasher, validators, roles, tokens).
        builder.Services.AddIdentityCore<AppUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders(); // password-reset / forced-change tokens

        // Refresh-session lifecycle (cookie + rotation/reuse) and password-reset options. The API
        // only ENQUEUES the reset email on the outbox; the worker sends it off the request path.
        builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Session"));
        builder.Services.AddSingleton<RefreshCookie>();
        builder.Services.AddScoped<SessionService>();
        builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection("PasswordReset"));

        // Internal service clients for the client-credentials grant (POST /auth/token).
        builder.Services.Configure<ServiceTokenOptions>(builder.Configuration.GetSection("ServiceToken"));

        // Declarative role grants (ROLE_GRANTS) — reconciled at login so a configured email holds its
        // role without a code change or a manual DB grant (and it survives a disposable-staging reset).
        builder.Services.AddSingleton(new RoleGrants(builder.Configuration["ROLE_GRANTS"]));
        builder.Services.AddScoped<RoleGrantReconciler>();

        // ES256 JWT issuer/validator; JwtBearer reads its parameters from the same instance.
        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtTokenService>((options, jwt) =>
            {
                // Keep the raw JWT claim names ("sub", "email", "roles") instead of the legacy URIs.
                options.MapInboundClaims = false;
                options.TokenValidationParameters = jwt.ValidationParameters();
            });

        return builder;
    }

    /// <summary>Map the module's vertical slices under its own prefix (<c>/auth</c>).</summary>
    public static IEndpointRouteBuilder MapIdentityModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");
        group.MapRegister();
        group.MapToken();
        group.MapLogin();
        group.MapRefresh();
        group.MapLogout();
        group.MapChangePassword();
        group.MapForgotPassword();
        group.MapResetPassword();
        group.MapWhoAmI();
        group.MapAccountSessions();
        group.MapJwks();
        group.MapCommandStatus<IdentityDbContext>(); // GET /auth/commands/{id}
        return app;
    }
}
