using ErrorOr;
using Iedora.Api.Identity.Contracts;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Api.Identity;

/// <summary>Identity's implementation of the <see cref="IIdentityApi"/> contract. Internal — other
/// modules only ever see the contract (this module's <c>Contracts</c> namespace), never this class,
/// its errors, or the identity tables it touches.</summary>
internal sealed class IdentityApi(IdentityDbContext db, UserManager<AppUser> users) : IIdentityApi
{
    public async Task<IReadOnlyList<UserSummary>> GetUsersAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        return await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new UserSummary(u.Id, u.Email!, u.DisplayName))
            .ToListAsync(ct);
    }

    public async Task<ErrorOr<Guid>> CreateUserAsync(NewUser input, CancellationToken ct)
    {
        var user = new AppUser { UserName = input.Email, Email = input.Email, DisplayName = input.Name };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded) return user.Id;

        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            return IdentityErrors.EmailTaken;
        // Password-policy etc. → validation errors, grouped by Identity's error code.
        return result.Errors.Select(e => Error.Validation($"identity.{e.Code}", e.Description)).ToList();
    }
}
