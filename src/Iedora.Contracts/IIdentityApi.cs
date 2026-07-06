namespace Iedora.Contracts;

/// <summary>A minimal projection of a user, for other modules to hydrate references held by id.</summary>
public sealed record UserSummary(Guid Id, string Email, string? Name);

/// <summary>
/// The <b>Identity</b> module's public cross-module API. Other modules depend on THIS contract, not
/// on the Identity module — implemented inside Identity, resolved via DI. Tenancy's admin reads use
/// it to resolve owner users without touching identity tables.
/// </summary>
public interface IIdentityApi
{
    /// <summary>Summaries for the given user ids (missing ids are simply absent from the result).</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
