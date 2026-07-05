// Aspire AppHost — orchestrates a Postgres container, a migration worker, and the auth service
// for local dev, and points OTLP telemetry at the EXISTING LGTM collector (:4318) rather than
// the Aspire dashboard, so traces/metrics/logs land in Grafana like the Bun services.
// Dashboard disabled: we observe through the existing Grafana/LGTM stack, not the Aspire
// dashboard (which also needs extra Kestrel/URL config to run headless). Disabling it means
// the OTLP endpoint we set below (the collector) is the only one the services see.
var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
{
    Args = args,
    DisableDashboard = true,
});

var postgres = builder.AddPostgres("postgres");
// Map the logical "authdb" onto Postgres's existing default "postgres" database (Aspire's
// AddDatabase doesn't run CREATE DATABASE; the migration worker builds the schema).
var authdb = postgres.AddDatabase("authdb", "postgres");

// Migration worker: applies EF migrations, then exits. The auth API waits for it to complete.
var migrations = builder.AddProject<Projects.Iedora_MigrationService>("migrations")
    .WithReference(authdb)
    .WaitFor(postgres)
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
    .WithEnvironment("OTEL_SERVICE_NAME", "iedora-migrations");

builder.AddProject<Projects.Iedora_Auth>("auth")
    .WithReference(authdb)                 // injects ConnectionStrings__authdb
    .WaitForCompletion(migrations)         // don't start serving until the schema is migrated
    .WithHttpEndpoint(port: 8090, name: "authhttp") // pinned port for the e2e test
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4318")
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf")
    .WithEnvironment("OTEL_SERVICE_NAME", "iedora-auth")
    .WithEnvironment("OTEL_RESOURCE_ATTRIBUTES", "service.namespace=iedora,deployment.environment.name=verify")
    .WithEnvironment("API_JWT_ISSUER", "https://api.iedora.com")
    .WithEnvironment("API_JWT_AUDIENCE", "iedora-api");

builder.Build().Run();
