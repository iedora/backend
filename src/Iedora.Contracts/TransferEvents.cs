namespace Iedora.Contracts;

/// <summary>
/// Integration events for the tenant-ownership-transfer saga. <see cref="CorrelationId"/> is the
/// same across both hops (it's the tracking command's id) — each module's inbox dedups on it.
/// </summary>

// Hop 1 — Tenancy → Identity: "create the new owner user."
public sealed record CreateUserRequested(Guid CorrelationId, Guid TenantId, string Email, string Name)
{
    public const string Type = "transfer.create_user_requested";
}

// Hop 2 — Identity → Tenancy: the outcome. UserId set ⇒ created; ErrorCode set ⇒ failed. Exactly one.
public sealed record UserProvisioned(Guid CorrelationId, Guid TenantId, Guid? UserId, string? ErrorCode)
{
    public const string Type = "transfer.user_provisioned";
}
