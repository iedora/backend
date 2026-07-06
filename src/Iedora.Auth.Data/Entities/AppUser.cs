using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Data;

/// <summary>The Identity user. Guid keys (uuid) + a display name beyond the Identity defaults.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    /// <summary>Admin-set forced password change. Read live by /auth/whoami so the client's
    /// guard stops redirecting the moment the change completes (not at token expiry).</summary>
    public bool MustChangePassword { get; set; }
}
