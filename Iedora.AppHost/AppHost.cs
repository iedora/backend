// Aspire AppHost — orchestrates a Postgres container + the auth service for local dev,
// and points the service's OTLP telemetry at the EXISTING LGTM collector (:4318) rather
// than the Aspire dashboard, so traces/metrics/logs land in Grafana like the Bun services.
// Dashboard disabled: we observe through the existing Grafana/LGTM stack, not the Aspire
// dashboard (which also needs extra Kestrel/URL config to run headless). Disabling it means
// the OTLP endpoint we set below (the collector) is the only one the service sees.
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = true,
});

var postgres = builder.AddPostgres("postgres");
// Map the logical "authdb" onto Postgres's existing default "postgres" database, so EF's
// EnsureCreated only builds the Identity SCHEMA (no CREATE DATABASE, which Aspire's
// AddDatabase doesn't run and which crash-loops the service).
var authdb = postgres.AddDatabase("authdb", "postgres");

builder.AddProject<Projects.Iedora_Auth>("auth")
    .WithReference(authdb)          // injects ConnectionStrings__authdb
    .WaitFor(postgres)              // wait for the SERVER (EF EnsureCreated makes the db + schema)
    .WithHttpEndpoint(port: 8090, name: "authhttp") // pinned port for the e2e test
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
    .WithEnvironment("OTEL_SERVICE_NAME", "iedora-auth")
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "service.namespace=iedora,deployment.environment.name=verify")
    .WithEnvironment("API_JWT_ISSUER", "https://api.iedora.com")
    .WithEnvironment("API_JWT_AUDIENCE", "iedora-api");

builder.Build().Run();
