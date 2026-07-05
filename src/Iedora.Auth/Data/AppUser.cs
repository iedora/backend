using Microsoft.AspNetCore.Identity;

namespace Iedora.Auth.Data;

/// <summary>The Identity user. Guid keys (uuid) + a display name beyond the Identity defaults.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
}
