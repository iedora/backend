using Iedora.Identity;
using Iedora.Tenancy;
using Microsoft.Extensions.Hosting;

// The single app-wide background worker. It composes each feature module's outbox-dispatch side —
// registering handlers only, mapping no endpoints — so it drains every module's outbox without
// hosting a web surface. Add more modules here as they land: builder.Add<Module>OutboxDispatch();
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddIdentityOutboxDispatch();
builder.AddTenancyMessaging();

builder.Build().Run();
