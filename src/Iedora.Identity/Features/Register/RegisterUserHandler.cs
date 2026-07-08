using ErrorOr;
using Framework.Commands;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

/// <summary>Executes <see cref="RegisterUserCommand"/> off the outbox: create the account with the
/// pre-computed password hash. Email uniqueness was pre-checked synchronously at the endpoint; a
/// lost race here surfaces as a Failed command (rather than a retry).</summary>
internal sealed class RegisterUserHandler(IdentityDbContext db, UserManager<AppUser> users, TimeProvider clock)
    : CommandHandler<IdentityDbContext, RegisterUserCommand>(db, clock)
{
    public override string Type => RegisterUserCommand.Type;

    protected override async Task<ErrorOr<string?>> ExecuteAsync(RegisterUserCommand cmd, CancellationToken ct)
    {
        var user = new AppUser
        {
            UserName = cmd.Email,
            Email = cmd.Email,
            DisplayName = cmd.DisplayName,
            PasswordHash = cmd.PasswordHash, // already hashed → the password is usable at login
        };
        var result = await users.CreateAsync(user);
        if (result.Succeeded)
        {
            Telemetry.Registrations.Add(1, new KeyValuePair<string, object?>("result", "created")); // a signup landed
            return $"/auth/users/{user.Id}";
        }

        Telemetry.Registrations.Add(1, new KeyValuePair<string, object?>("result", "rejected")); // lost email-uniqueness race
        if (result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            return Error.Conflict("auth.email_taken", "An account with that email already exists.");
        return result.Errors.Select(e => Error.Validation($"auth.{e.Code}", e.Description)).ToList();
    }
}
