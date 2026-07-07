namespace Framework.Email;

/// <summary>SMTP settings, bound by the consumer from an "Smtp" config section (env-overridable).
/// Defaults target a local dev catcher (Mailpit/smtp4dev); set From/host per deployment.</summary>
public sealed class SmtpSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseStartTls { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = "no-reply@localhost";
    public string FromName { get; set; } = "";
}
