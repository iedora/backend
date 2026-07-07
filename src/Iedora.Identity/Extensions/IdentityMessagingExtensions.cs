using Framework.Inbox;
using Framework.Outbox;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Iedora.Identity;

public static class IdentityMessagingExtensions
{
    /// <summary>
    /// Register the Identity module's message dispatch into a host (the generic app worker):
    /// IdentityDbContext, SMTP config, Identity Core (so saga handlers can create users off the
    /// request path), and its outbox/inbox handlers. Self-contained — the worker never references
    /// the API web project.
    /// </summary>
    public static IHostApplicationBuilder AddIdentityOutboxDispatch(this IHostApplicationBuilder builder)
    {
        builder.AddIdentityDb();
        builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
        builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));

        // The worker has no ASP.NET Identity pipeline of its own, so add UserManager here.
        builder.Services.AddIdentityCore<AppUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityDbContext>();

        builder.Services.AddIdentityMessagingHandlers();
        return builder;
    }

    /// <summary>Register the Identity outbox/inbox dispatchers + handlers onto an existing service
    /// collection (DbContext + UserManager registered separately). Lets the API's integration-test
    /// host drive the pipeline in-process while keeping the internal handlers internal.</summary>
    public static IServiceCollection AddIdentityMessagingHandlers(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IEmailSender, MailKitEmailSender>();

        services.AddOutbox<IdentityDbContext>();
        services.AddInbox<IdentityDbContext>();

        services.AddScoped<IOutboxHandler, PasswordResetEmailHandler>();
        services.AddScoped<IOutboxHandler, RegisterUserHandler>();     // async write: create account
        services.AddScoped<IOutboxHandler, ChangePasswordHandler>();      // async write: change password
        services.AddScoped<IOutboxHandler, ResetPasswordHandler>();       // async write: reset password
        services.AddScoped<IOutboxHandler, SetUserPasswordHandler>();     // admin: set temp password
        services.AddScoped<IOutboxHandler, ForcePasswordChangeHandler>(); // admin: force change at next login
        services.AddScoped<IOutboxHandler, CreateUserRelay>();         // transfer saga: → Identity inbox
        services.AddScoped<IInboxHandler, CreateUserInboxHandler>(); //   creates the new owner user
        return services;
    }
}
