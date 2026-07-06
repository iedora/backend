using Iedora.Auth.Messaging;
using Microsoft.Extensions.Hosting;

// The single app-wide background worker. It composes each service's outbox-dispatch module — it
// only references the non-web messaging modules, never a service's web project, and stays generic.
// Add more services here as they land: builder.Add<Service>OutboxDispatch();
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddAuthOutboxDispatch();

builder.Build().Run();
