using ErrorOr;

namespace Iedora.Api.Identity.Contracts;

/// <summary>A minimal projection of a user, for other modules to hydrate references held by id.</summary>
public sealed record UserSummary(Guid Id, string Email, string? Name);

/// <summary>A user to create on another module's behalf (e.g. tenant ownership transfer).</summary>
public sealed record NewUser(string Email, string Name, string Password);

/// <summary>
/// The <b>Identity</b> module's public API for other modules. Everything under this module's
/// <c>Contracts</c> namespace is external-use by definition: a module may import another module's
/// <c>.Contracts</c>, and NOTHING else of it. Implemented internally, resolved via DI.
/// </summary>
public interface IIdentityApi
{
    /// <summary>Summaries for the given user ids (missing ids are simply absent from the result).</summary>
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>Create a user and return its id. Conflict (email taken) or weak-password failures
    /// come back as errors — the caller maps them to a response.</summary>
    Task<ErrorOr<Guid>> CreateUserAsync(NewUser user, CancellationToken ct);
}
