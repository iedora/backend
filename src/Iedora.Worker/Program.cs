using Framework.Inbox;
using Framework.Outbox;
using Iedora.Identity;
using Iedora.Menus;
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
builder.AddMenuMaintenance(); // periodic dedup-marker pruning (view_seen, item_view_seen)

// Retention pruning runs only here (never in the API host): drop dispatched outbox rows and expired
// inbox dedup rows per registered context, on the same sweeper as the menu dedup markers.
builder.Services.Configure<OutboxRetentionOptions>(builder.Configuration.GetSection(OutboxRetentionOptions.SectionName));
builder.Services.Configure<InboxRetentionOptions>(builder.Configuration.GetSection(InboxRetentionOptions.SectionName));
builder.Services
    .AddOutboxRetention<IdentityDbContext>()
    .AddOutboxRetention<TenancyDbContext>()
    .AddInboxRetention<IdentityDbContext>()
    .AddInboxRetention<TenancyDbContext>()
    .AddInboxRetention<NotificationsDbContext>();

builder.Build().Run();
