using Framework.Inbox;
using Microsoft.EntityFrameworkCore;

namespace Framework.Notifications;

/// <summary>The Notifications service's store, under the <c>notifications</c> schema. It owns only an
/// idempotent-consumer INBOX (no outbox/commands) — it consumes send-requests and dispatches them.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public const string Schema = "notifications";

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.MapInbox();
    }
}
