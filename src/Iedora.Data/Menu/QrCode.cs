namespace Iedora.Data;

/// <summary>
/// A physical QR sticker code. A cross-tenant, staff-managed registry: codes exist before they're
/// bound to a restaurant, and survive its deletion (the FK is <c>SET NULL</c>, so a sticker is
/// reusable). <see cref="Code"/> is the normalized (lowercase) primary key.
/// </summary>
public sealed class QrCode
{
    public string Code { get; set; } = "";
    public Guid? RestaurantId { get; set; }      // null = unbound
    public string? Label { get; set; }
    public DateTimeOffset? BoundAt { get; set; }  // null = unbound
    public DateTimeOffset CreatedAt { get; set; }
}
