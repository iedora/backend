using ErrorOr;

namespace Iedora.Kernel;

/// <summary>
/// The shared-kernel error catalog: errors that belong to NO single module, so every module may
/// use them. This is the ONLY error catalog a module may reference besides its own — a module must
/// never reach into another module's catalog (e.g. Tenancy must not use <c>IdentityErrors</c>).
/// </summary>
public static class CommonErrors
{
    /// <summary>The request carries no usable identity (missing/invalid <c>sub</c>). Maps to 401.
    /// Distinct from a module's domain errors — any authed endpoint in any module can hit it.</summary>
    public static readonly Error Unauthenticated =
        Error.Unauthorized("auth.unauthenticated", "The request is not authenticated.");
}
