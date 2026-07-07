namespace Iedora.Identity.Contracts;

/// <summary>Transfer saga, hop 2 — Identity → Tenancy: the outcome. Published by Identity, consumed
/// by Tenancy. <see cref="UserId"/> set ⇒ created; <see cref="ErrorCode"/> set ⇒ failed (exactly one).
/// Shares the saga's <see cref="CorrelationId"/>.</summary>
public sealed record UserProvisioned(Guid CorrelationId, Guid TenantId, Guid? UserId, string? ErrorCode)
{
    public const string Type = "transfer.user_provisioned";
}
