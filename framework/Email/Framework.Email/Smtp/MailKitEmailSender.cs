using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Framework.Email;

/// <summary>MailKit SMTP sender (System.Net.Mail.SmtpClient is soft-deprecated). One client per
/// send; retries belong to the caller's outbox, so this just does the transport.</summary>
public sealed class MailKitEmailSender(IOptions<SmtpSettings> options) : IEmailSender
{
    private readonly SmtpSettings _smtp = options.Value;

    public async Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_smtp.FromName, _smtp.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

        using var client = new SmtpClient();
        var socket = _smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_smtp.Host, _smtp.Port, socket, ct);
        if (!string.IsNullOrEmpty(_smtp.User))
            await client.AuthenticateAsync(_smtp.User, _smtp.Password ?? string.Empty, ct);
        await client.SendAsync(message, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
