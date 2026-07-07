namespace Iedora.Identity;

/// <summary>
/// A refresh-token session. Rotation chains share a <see cref="FamilyId"/>; presenting an
/// already-rotated token (reuse) burns the whole family. The raw refresh token is never
/// stored — only its SHA-256 digest in <see cref="TokenHash"/>. A session is live only while
/// <see cref="RevokedAt"/> is null AND now &lt; both <see cref="ExpiresAt"/> (sliding) and
/// <see cref="AbsoluteExpiresAt"/> (hard cap from first login).
/// </summary>
public sealed class Session
{
    public Guid Id { get; set; }
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public Guid? TenantId { get; set; }

    /// <summary>SHA-256 of the opaque refresh token. Unique; never the raw token.</summary>
    public byte[] TokenHash { get; set; } = [];

    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Successor session id, set when this token is rotated. Non-null ⇒ already used.</summary>
    public Guid? ReplacedBy { get; set; }

    public string? UserAgent { get; set; }
    public string? Ip { get; set; }

    /// <summary>Live at <paramref name="now"/>: not revoked, and before both expiries.</summary>
    public bool IsLive(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now && AbsoluteExpiresAt > now;

    /// <summary>Rotated/spent: a successor was minted or it was revoked. The reuse signal.</summary>
    public bool IsRotated => ReplacedBy is not null || RevokedAt is not null;
}
