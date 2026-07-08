namespace Framework.Notifications.Contracts;

/// <summary>A request to deliver one already-rendered email. Publishers (e.g. Identity's
/// forgot-password) render the content and put this on THEIR outbox; the Notifications service
/// relays it into its inbox (dedup on <see cref="CorrelationId"/>) and sends it. Transport only —
/// the sender owns the wording/template.</summary>
public sealed record EmailRequested(Guid CorrelationId, string To, string Subject, string HtmlBody)
{
    public const string Type = "notifications.email_requested";
}
