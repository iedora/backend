using System.Reflection;
using Iedora.Auth.Data;
using Iedora.Auth.Features.Jwks;
using Iedora.Auth.Features.Login;
using Iedora.Auth.Features.Register;
using Iedora.Auth.Features.WhoAmI;
using Iedora.Auth.Observability;
using Iedora.Auth.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry (traces + metrics + logs) + health checks +
// HttpClient resilience — batteries-included observability, pointed at the collector.
builder.AddServiceDefaults();

// Extend OTel with our own business ActivitySource + Meter (auth.login span, tokens_issued).
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
    .AddEntityFrameworkStores<AuthDbContext>();

// Built-in JwtBearer validation, using the SAME ES256 key the token service signs with.
var jwt = new JwtTokenService(builder.Configuration);
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Keep the raw JWT claim names ("sub", "email", "roles") instead of remapping
        // them to the legacy XML-schema URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwt.ValidationParameters();
    });
builder.Services.AddAuthorization();
builder.Services.AddProblemDetails();

// OpenAPI document — the source of truth for the generated frontend client. Emitted at
// build time (see the .csproj) and also served at /openapi/v1.json for live tooling.
builder.Services.AddOpenApi();

// Build the Identity schema on boot (greenfield; EF migrations are a follow-up). Skipped
// under the build-time OpenAPI generator (GetDocument.Insider runs the host but has no
// database) — the DbContext is otherwise only resolved per-request, so doc generation
// needs no Postgres.
var generatingOpenApiDoc = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";
if (!generatingOpenApiDoc)
    builder.Services.AddHostedService<SchemaInitializer>();

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
auth.MapWhoAmI();
auth.MapJwks();

app.Run();

// Exposed for WebApplicationFactory<Program> integration tests.
public partial class Program;
