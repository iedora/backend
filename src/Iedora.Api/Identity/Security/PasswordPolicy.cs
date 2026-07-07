using ErrorOr;
using Iedora.Data;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Api.Security;

/// <summary>
/// Runs ASP.NET Identity's configured password validators synchronously, so a policy-violating
/// password is rejected with a 400 at the endpoint — before the async command is submitted. (The
/// DataAnnotation on the request only enforces length; this covers digit/case/symbol rules.)
/// </summary>
public static class PasswordPolicy
{
    public static async Task<ErrorOr<Success>> ValidateAsync(
        UserManager<AppUser> users, AppUser user, string password)
    {
        var failures = new List<Error>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, password);
            if (!result.Succeeded)
                failures.AddRange(result.Errors.Select(e => Error.Validation($"auth.{e.Code}", e.Description)));
        }
        return failures.Count > 0 ? failures : Result.Success;
    }
}
