using Iedora.Auth.Data;
using Iedora.MigrationService;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Aspire defaults (OpenTelemetry + health) and the Npgsql-wired AuthDbContext.
builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AuthDbContext>("authdb");

builder.Services.AddHostedService<MigrationWorker>();

builder.Build().Run();
