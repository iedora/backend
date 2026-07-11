using Framework.Media;
using Iedora.Api;
using Iedora.Identity;
using Iedora.Identity.Contracts;
using Iedora.Menus;
using Iedora.Tenancy;

var builder = WebApplication.CreateBuilder(args);

// Aspire ServiceDefaults: OpenTelemetry (traces + metrics + logs) + health checks + HttpClient
// resilience — batteries-included observability, pointed at the collector.
builder.AddServiceDefaults();

// Feature modules — each owns its Postgres schema, its DI, and (below) its endpoints. This is the
// whole modular-monolith surface: add a module here and it self-wires.
builder.AddIdentityModule();
builder.AddTenancyModule();
builder.AddMenuModule();
builder.AddMediaModule();

// Host-level cross-cutting.
builder.Services.AddSingleton(TimeProvider.System); // testable clock (session TTLs, token expiry)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Policies.Admin, policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy(Policies.Service, policy => policy.RequireClaim("typ", TokenTypes.Service));
});
// NOTE: AddValidation() is called inside each module (AddIdentityModule/AddTenancyModule), not here —
// the .NET 10 validation source generator must run in the assembly that defines the endpoints.
builder.Services.AddProblemDetails();
// Harden inbound JSON (.NET 10): refuse a body with duplicate property names. A repeated key is
// ambiguous — which value wins is silent — so we reject rather than guess. Matters most for the staff
// menu-JSON import (untrusted admin-pasted documents); applies to every request body binding.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.AllowDuplicateProperties = false);
builder.Services.AddOpenApi();        // build-time source of truth for the generated frontend client
builder.AddIedoraRateLimiter();       // per-IP/per-user throttling (auth brute-force, upload/DoS)
builder.AddIedoraCors();              // browser SPA consumers (admin dashboard, front-office)

// NOTE: schema is applied by the Iedora.MigrationService worker (the AppHost gates this API on its
// completion), so the API never migrates on startup — no DB access before serving.

var app = builder.Build();

// First: resolve the real client IP from a trusted proxy's X-Forwarded-For (so rate-limit
// partitioning is per-client, not per-proxy). No-op unless a proxy is configured.
app.UseForwardedHeaders();
app.UseCors(CorsExtensions.PolicyName); // allow the configured SPA origins (before auth/endpoints)

// Aspire health endpoints (/health, /alive) — filtered out of tracing by ServiceDefaults.
app.MapDefaultEndpoints();
app.MapOpenApi();

app.UseAuthentication();
app.UseRateLimiter();   // after auth so the per-user upload limit can read the caller's identity
app.UseAuthorization();

// Each module mounts its vertical slices under its own prefix (/auth, /tenancy).
app.MapIdentityModule();
app.MapTenancyModule();
app.MapMenuModule();
app.MapMediaModule();

app.Run();

// Exposed for WebApplicationFactory<Program> integration tests.
public partial class Program;
