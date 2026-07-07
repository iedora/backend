namespace Iedora.Tenancy.Contracts;

/// <summary>Transfer saga, hop 1 — Tenancy → Identity: "create the new owner user." Published by
/// Tenancy, consumed by Identity. <see cref="CorrelationId"/> (the tracking command's id) is the
/// same across both hops; each module's inbox dedups on it.</summary>
public sealed record CreateUserRequested(Guid CorrelationId, Guid TenantId, string Email, string Name)
{
    public const string Type = "transfer.create_user_requested";
}
