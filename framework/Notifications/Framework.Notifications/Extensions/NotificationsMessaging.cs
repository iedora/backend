using Framework.Email;
using Framework.Inbox;
using Framework.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Framework.Notifications;

public static class NotificationsMessaging
{
    /// <summary>
    /// Register the Notifications service into a host (the generic worker): its inbox DbContext, the
    /// SMTP settings, and its relay + delivery handlers. Self-contained — the worker just calls this.
    /// </summary>
    public static IHostApplicationBuilder AddNotificationsMessaging(this IHostApplicationBuilder builder)
    {
        builder.AddNotificationsDb();
        builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
        builder.Services.AddNotificationsHandlers();
        return builder;
    }

    /// <summary>Register the Notifications inbox + its relay/delivery handlers + the SMTP sender onto
    /// an existing service collection (the DbContext is registered separately). Lets the integration-
    /// test host drive delivery in-process.</summary>
    public static IServiceCollection AddNotificationsHandlers(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddInbox<NotificationsDbContext>();
        services.AddSmtpEmail();

        services.AddScoped<IOutboxHandler, EmailRequestedRelay>();       // publisher's outbox → this inbox
        services.AddScoped<IInboxHandler, EmailRequestedInboxHandler>(); //   deliver over SMTP
        return services;
    }
}
