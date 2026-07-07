using Iedora.Api.Identity;
using Iedora.Api.Shared;
using Iedora.Api.Tenancy;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry (traces + metrics + logs) + health checks + HttpClient
// resilience — batteries-included observability, pointed at the collector.
builder.AddServiceDefaults();

// Feature modules — each owns its Postgres schema, its DI, and (below) its endpoints. This is the
// whole modular-monolith surface: add a module here and it self-wires.
builder.AddIdentityModule();
builder.AddTenancyModule();

// Host-level cross-cutting.
builder.Services.AddSingleton(TimeProvider.System); // testable clock (session TTLs, token expiry)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy(Policies.Service, policy => policy.RequireClaim("typ", TokenTypes.Service));
});
builder.Services.AddValidation();     // built-in minimal-API DataAnnotations validation (.NET 10)
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();        // build-time source of truth for the generated frontend client

// NOTE: schema is applied by the Iedora.MigrationService worker (the AppHost gates this API on its
// completion), so the API never migrates on startup — no DB access before serving.

var app = builder.Build();

// Aspire health endpoints (/health, /alive) — filtered out of tracing by ServiceDefaults.
app.MapDefaultEndpoints();
app.MapOpenApi();

app.UseAuthentication();
app.UseAuthorization();

// Each module mounts its vertical slices under its own prefix (/auth, /tenancy).
app.MapIdentityModule();
app.MapTenancyModule();

app.Run();

// Exposed for WebApplicationFactory<Program> integration tests.
public partial class Program;
