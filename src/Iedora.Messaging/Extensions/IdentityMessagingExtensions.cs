using Framework.Outbox;
using Iedora.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Iedora.Messaging;

public static class IdentityMessagingExtensions
{
    /// <summary>
    /// Register the Identity module's outbox dispatcher into a host (the generic app worker): the
    /// IdentityDbContext (which owns the outbox table), the SMTP sender, the outbox handlers, and
    /// the per-context dispatcher. Self-contained — the worker just calls this, so it never
    /// references the API web project.
    /// </summary>
    public static IHostApplicationBuilder AddIdentityOutboxDispatch(this IHostApplicationBuilder builder)
    {
        builder.AddIdentityDb();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
        builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
        builder.Services.AddSingleton<IEmailSender, MailKitEmailSender>();

        builder.Services.AddOutbox<IdentityDbContext>();
        builder.Services.AddScoped<IOutboxHandler, PasswordResetEmailHandler>();

        return builder;
    }
}
