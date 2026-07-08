using Iedora.Identity;
using Framework.Notifications;
using Iedora.Tenancy;

// The single app-wide background worker. It composes each feature module's outbox-dispatch side —
// registering handlers only, mapping no endpoints — so it drains every module's outbox without
// hosting a web surface. Add more modules here as they land: builder.Add<Module>OutboxDispatch();
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddIdentityOutboxDispatch();
builder.AddTenancyMessaging();
builder.AddNotificationsMessaging();

builder.Build().Run();
