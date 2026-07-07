using ErrorOr;
using Framework.Commands;
using Iedora.Data;

namespace Iedora.Messaging;

/// <summary>Executes <see cref="SetUserPasswordCommand"/> off the outbox: swap in the temporary hash,
/// flag the account must-change, roll the security stamp, and revoke every session.</summary>
internal sealed class SetUserPasswordHandler(IdentityDbContext db, TimeProvider clock)
    : CommandHandler<IdentityDbContext, SetUserPasswordCommand>(db, clock)
{
    public override string Type => SetUserPasswordCommand.Type;

    protected override Task<ErrorOr<string?>> ExecuteAsync(SetUserPasswordCommand cmd, CancellationToken ct) =>
        PasswordCredential.ApplyAsync(
            Db, Clock, cmd.UserId, cmd.PasswordHash, mustChangePassword: true, keepFamilyId: null, ct);
}
