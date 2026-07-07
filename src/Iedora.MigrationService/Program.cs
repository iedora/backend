using Iedora.Data;
using Iedora.MigrationService;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Aspire defaults (OpenTelemetry + health) and every module's DbContext — this worker owns the
// whole schema, so it migrates all of them.
builder.AddServiceDefaults();
builder.AddIdentityDb();
builder.AddTenancyDb();
builder.AddMenuDb();

builder.Services.AddHostedService<MigrationWorker>();

builder.Build().Run();
