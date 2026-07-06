using Iedora.Api.Identity;
using Iedora.Api.Features.ChangePassword;
using Iedora.Api.Features.ForgotPassword;
using Iedora.Api.Features.Jwks;
using Iedora.Api.Features.Login;
using Iedora.Api.Features.Logout;
using Iedora.Api.Features.Refresh;
using Iedora.Api.Features.Register;
using Iedora.Api.Features.ResetPassword;
using Iedora.Api.Features.WhoAmI;
using Iedora.Api.Observability;
using Iedora.Api.Security;
using Iedora.Api.Sessions;
using Iedora.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Iedora.Api.Identity;

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
        // Business telemetry (auth.login span, session metrics) on top of ServiceDefaults' OTel.
        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddSource(Telemetry.ActivitySourceName))
            .WithMetrics(m => m.AddMeter(Telemetry.MeterName));

        builder.AddIdentityDb();

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
        group.MapLogin();
        group.MapRefresh();
        group.MapLogout();
        group.MapChangePassword();
        group.MapForgotPassword();
        group.MapResetPassword();
        group.MapWhoAmI();
        group.MapJwks();
        return app;
    }
}
