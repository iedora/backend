using Iedora.Identity.Contracts;
using Microsoft.AspNetCore.Identity;

namespace Iedora.Identity;

/// <summary>
/// Declarative role grants parsed from the <c>ROLE_GRANTS</c> config string, e.g.
/// <c>"admin=me@x.com,you@x.com; staff=@x.com"</c> — each group is <c>role=identity[,identity…]</c>,
/// and an identity is an exact email or a <c>@domain</c> suffix. Config-driven so who is staff is a
/// deploy setting, not a code change; empty/unset means no grants. Applied at login by
/// <see cref="RoleGrantReconciler"/>, so it survives DB resets (staging is disposable).
/// </summary>
public sealed class RoleGrants
{
    // role -> lowercased identities (exact emails and "@domain" suffixes).
    private readonly Dictionary<string, HashSet<string>> _byRole = new(StringComparer.OrdinalIgnoreCase);

    public RoleGrants(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return;
        foreach (var group in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = group.IndexOf('=');
            if (eq <= 0) continue; // no role= prefix → skip
            var role = group[..eq].Trim();
            var ids = group[(eq + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0) continue;
            if (!_byRole.TryGetValue(role, out var set)) _byRole[role] = set = new(StringComparer.OrdinalIgnoreCase);
            foreach (var id in ids) set.Add(id.ToLowerInvariant());
        }
    }

    /// <summary>The roles granted to <paramref name="email"/> — an exact match, or a <c>@domain</c> suffix.</summary>
    public IEnumerable<string> RolesFor(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) yield break;
        var e = email.Trim().ToLowerInvariant();
        foreach (var (role, ids) in _byRole)
            if (ids.Contains(e) || ids.Any(id => id.StartsWith('@') && e.EndsWith(id, StringComparison.Ordinal)))
                yield return role;
    }
}

/// <summary>Ensures a user holds the roles <see cref="RoleGrants"/> grants their email — creating the
/// role if it doesn't exist yet (roles aren't seeded). Called at login; idempotent (no-op once granted).</summary>
public sealed class RoleGrantReconciler(
    RoleGrants grants, RoleManager<IdentityRole<Guid>> roles, UserManager<AppUser> users)
{
    public async Task ApplyAsync(AppUser user)
    {
        foreach (var role in grants.RolesFor(user.Email))
        {
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole<Guid>(role));
            if (!await users.IsInRoleAsync(user, role))
                await users.AddToRoleAsync(user, role);
        }
    }
}
