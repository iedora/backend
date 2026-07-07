using ErrorOr;
using Framework.Commands;
using Iedora.Data;

namespace Iedora.Messaging;

/// <summary>Executes <see cref="ResetPasswordCommand"/> off the outbox: swap in the new hash, roll the
/// security stamp (which also invalidates the reset token just used), and revoke every session. Token
/// verification happened synchronously at the endpoint.</summary>
internal sealed class ResetPasswordHandler(IdentityDbContext db, TimeProvider clock)
    : CommandHandler<IdentityDbContext, ResetPasswordCommand>(db, clock)
{
    public override string Type => ResetPasswordCommand.Type;

    protected override Task<ErrorOr<string?>> ExecuteAsync(ResetPasswordCommand cmd, CancellationToken ct) =>
        PasswordCredential.ApplyAsync(
            Db, Clock, cmd.UserId, cmd.PasswordHash, clearMustChangePassword: false, keepFamilyId: null, ct);
}
