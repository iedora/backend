using Iedora.Identity;

namespace Iedora.Api.IntegrationTests;

public sealed record SentEmail(string To, string Subject, string HtmlBody);

/// <summary>Test double for <see cref="IEmailSender"/> — records sent mail instead of hitting SMTP,
/// so tests can assert the outbox actually dispatched. Shared across the assembly; cleared per test.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly Lock _gate = new();
    private readonly List<SentEmail> _sent = [];

    public IReadOnlyList<SentEmail> Sent
    {
        get { lock (_gate) return _sent.ToArray(); }
    }

    public void Clear()
    {
        lock (_gate) _sent.Clear();
    }

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct)
    {
        lock (_gate) _sent.Add(new SentEmail(to, subject, htmlBody));
        return Task.CompletedTask;
    }
}
