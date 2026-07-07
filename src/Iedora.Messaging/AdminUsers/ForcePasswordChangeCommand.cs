namespace Iedora.Messaging;

/// <summary>Admin action: force a target user to change their password at next login. Leaves the
/// current password intact (no hash) but revokes every session, so they must re-authenticate and get
/// routed through the change-password screen.</summary>
public sealed record ForcePasswordChangeCommand(Guid UserId)
{
    public const string Type = "identity.admin.force-password-change";
}
