namespace Iedora.Tenancy;

/// <summary>Create a tenant owned by <see cref="OwnerUserId"/>. Enqueued by the accept endpoint
/// (after sync validation), executed by <c>CreateTenantHandler</c> off the outbox.</summary>
public sealed record CreateTenantCommand(string Name, Guid OwnerUserId)
{
    /// <summary>The outbox message type / command discriminator (shared by endpoint + handler).</summary>
    public const string Type = "tenancy.create-tenant";
}
