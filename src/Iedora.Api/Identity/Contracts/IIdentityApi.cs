namespace Iedora.Api.Identity.Contracts;

/// <summary>A minimal projection of a user, for other modules to hydrate references held by id.</summary>
public sealed record UserSummary(Guid Id, string Email, string? Name);

/// <summary>
/// The <b>Identity</b> module's public API for other modules. Everything under this module's
/// <c>Contracts</c> namespace is external-use by definition: a module may import another module's
/// <c>.Contracts</c>, and NOTHING else of it. Implemented internally, resolved via DI.
/// </summary>
public interface IIdentityApi
{
    /// <summary>Summaries for the given user ids (missing ids are simply absent from the result).</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);
}
