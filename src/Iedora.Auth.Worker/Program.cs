using Framework.Outbox;
using Iedora.Auth.Data;
using Iedora.Auth.Email;
using Iedora.Auth.Features.ForgotPassword;
using Microsoft.Extensions.Hosting;

// Dedicated outbox dispatcher. The API only ENQUEUES outbox messages; this worker drains them
// (FOR UPDATE SKIP LOCKED, so it scales to N replicas) and runs the handlers — keeping slow
// effects (SMTP) off the API's request path.
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<AuthDbContext>("authdb");

builder.Services.AddSingleton(TimeProvider.System);

// Email sink + the auth outbox handlers.
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();

// The dispatcher + background poll loop.
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.AddOutbox<AuthDbContext>();
builder.Services.AddScoped<IOutboxHandler, PasswordResetEmailHandler>();

builder.Build().Run();
