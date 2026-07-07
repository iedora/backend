namespace Framework.Email;

/// <summary>Sends one transactional email. Transport only — pair with a transactional outbox so
/// retries live there, not here.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct);
}
