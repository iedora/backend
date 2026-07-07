using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Framework.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>Register the MailKit SMTP <see cref="IEmailSender"/>. Bind <see cref="SmtpSettings"/>
    /// from your "Smtp" config section separately (Configure&lt;SmtpSettings&gt;(...)).</summary>
    public static IServiceCollection AddSmtpEmail(this IServiceCollection services)
    {
        services.TryAddSingleton<IEmailSender, MailKitEmailSender>();
        return services;
    }
}
