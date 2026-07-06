using Iedora.Auth.Common;
using Iedora.Auth.Data;
using Iedora.Auth.Features.ChangePassword;
using Iedora.Auth.Features.ForgotPassword;
using Iedora.Auth.Features.Jwks;
using Iedora.Auth.Features.Login;
using Iedora.Auth.Features.Logout;
using Iedora.Auth.Features.Refresh;
using Iedora.Auth.Features.Register;
using Iedora.Auth.Features.ResetPassword;
using Iedora.Auth.Features.Tenants;
using Iedora.Auth.Features.WhoAmI;
using Iedora.Auth.Observability;
using Iedora.Auth.Security;
using Iedora.Auth.Sessions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry (traces + metrics + logs) + health checks +
// HttpClient resilience — batteries-included observability, pointed at the collector.
builder.AddServiceDefaults();

// Extend OTel with our own business ActivitySource + Meter (auth.login span, session metrics).
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Telemetry.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(Telemetry.MeterName));

// Postgres via the Aspire integration: registers AuthDbContext from ConnectionStrings:authdb,
// plus DB health checks and CLIENT db spans — no hand-wiring.
builder.AddNpgsqlDbContext<AuthDbContext>("authdb");

// Full ASP.NET Core Identity over EF Core (its PasswordHasher, validators, roles).
builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders(); // password-reset / forced-change tokens

// Testable clock (FakeTimeProvider in tests) — drives token expiry + session TTLs.
builder.Services.AddSingleton(TimeProvider.System);

// Refresh-session lifecycle: cookie settings (env-overridable), the cookie reader/writer,
// and the session service (rotation + reuse detection).
builder.Services.Configure<SessionSettings>(builder.Configuration.GetSection("Session"));
builder.Services.AddSingleton<RefreshCookie>();
builder.Services.AddScoped<SessionService>();

// Password-reset: the API only ENQUEUES the email on the DbContext (same tx as the domain
// change). The dedicated Iedora.Auth.Worker drains the outbox and sends it — the API's request
// path never touches SMTP.
builder.Services.Configure<PasswordResetOptions>(builder.Configuration.GetSection("PasswordReset"));

// ES256 JWT issuer/validator, DI-managed so it picks up TimeProvider. JwtBearer is configured
// from the same instance (deferred to post-build so DI is available).
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtTokenService>((options, jwt) =>
    {
        // Keep the raw JWT claim names ("sub", "email", "roles") instead of the legacy URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwt.ValidationParameters();
    });
builder.Services.AddAuthorization();

// Built-in minimal-API request validation (.NET 10) — DataAnnotations on the request records.
builder.Services.AddValidation();
builder.Services.AddProblemDetails();

// OpenAPI document — the source of truth for the generated frontend client. Emitted at
// build time (see the .csproj) and also served at /openapi/v1.json for live tooling.
builder.Services.AddOpenApi();

// NOTE: schema is applied by the Iedora.MigrationService worker (the AppHost gates this API
// on its completion), so the API never migrates on startup — no DB access before serving.

var app = builder.Build();

// Aspire health endpoints (/health, /alive) — filtered out of tracing by ServiceDefaults.
app.MapDefaultEndpoints();
app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

// Vertical slices under /auth.
var auth = app.MapGroup("/auth");
auth.MapRegister();
auth.MapLogin();
auth.MapRefresh();
auth.MapLogout();
auth.MapChangePassword();
auth.MapForgotPassword();
auth.MapResetPassword();
auth.MapTenants();
auth.MapWhoAmI();
auth.MapJwks();

app.Run();

// Exposed for WebApplicationFactory<Program> integration tests.
public partial class Program;
