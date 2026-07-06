namespace Iedora.Tenancy;

/// <summary>The tenant-ownership-transfer saga: the accept endpoint records a tracking command of
/// this type + emits <c>CreateUserRequested</c>; the command stays <c>Pending</c> across both hops
/// and is completed by <see cref="UserProvisionedInboxHandler"/>.</summary>
public static class TransferSaga
{
    public const string CommandType = "tenancy.transfer";
}
