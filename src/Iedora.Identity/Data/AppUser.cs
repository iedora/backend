using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

/// <summary>The Identity user. Guid keys (uuid) + a display name beyond the Identity defaults.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    /// <summary>Admin-set forced password change. Read live by /auth/whoami so the client's
    /// guard stops redirecting the moment the change completes (not at token expiry).</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>When the account was created (account age in the staff Users CRM). DB-defaulted.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last password change; null until the first change after registration ("last password
    /// change" in the staff CRM + owner settings). Stamped by <see cref="PasswordCredential"/> and
    /// the admin set-password action.</summary>
    public DateTimeOffset? PasswordChangedAt { get; set; }
}
