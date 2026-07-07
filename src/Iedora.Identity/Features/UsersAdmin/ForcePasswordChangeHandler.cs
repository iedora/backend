using ErrorOr;
using Framework.Commands;
using Iedora.Data;

namespace Iedora.Identity;

/// <summary>Executes <see cref="ForcePasswordChangeCommand"/> off the outbox: flag the account
/// must-change, roll the security stamp, and revoke every session — without touching the password.</summary>
internal sealed class ForcePasswordChangeHandler(IdentityDbContext db, TimeProvider clock)
    : CommandHandler<IdentityDbContext, ForcePasswordChangeCommand>(db, clock)
{
    public override string Type => ForcePasswordChangeCommand.Type;

    protected override Task<ErrorOr<string?>> ExecuteAsync(ForcePasswordChangeCommand cmd, CancellationToken ct) =>
        PasswordCredential.ApplyAsync(
            Db, Clock, cmd.UserId, passwordHash: null, mustChangePassword: true, keepFamilyId: null, ct);
}
