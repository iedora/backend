using ErrorOr;
using Framework.Commands;

namespace Iedora.Identity;

/// <summary>Executes <see cref="ChangePasswordCommand"/> off the outbox: swap in the new hash, roll
/// the security stamp, clear any forced-change flag, and revoke the user's other sessions (keeping the
/// current device). All authorization + validation happened synchronously at the endpoint.</summary>
internal sealed class ChangePasswordHandler(IdentityDbContext db, TimeProvider clock)
    : CommandHandler<IdentityDbContext, ChangePasswordCommand>(db, clock)
{
    public override string Type => ChangePasswordCommand.Type;

    protected override Task<ErrorOr<string?>> ExecuteAsync(ChangePasswordCommand cmd, CancellationToken ct) =>
        PasswordCredential.ApplyAsync(
            Db, Clock, cmd.UserId, cmd.PasswordHash, mustChangePassword: false, cmd.KeepFamilyId, ct);
}
