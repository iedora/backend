namespace Iedora.Identity;

/// <summary>
/// Refresh-session + cookie settings, bound from the "Session" config section (env-overridable).
/// Defaults mirror the Bun service so the deployed cookie/TTL contract is unchanged.
/// </summary>
public sealed class SessionSettings
{
    /// <summary>Refresh cookie name. Bun default.</summary>
    public string RefreshCookieName { get; set; } = "iedora_refresh";

    /// <summary>Cookie domain; empty ⇒ host-only (omit the Domain attribute).</summary>
    public string CookieDomain { get; set; } = "";

    /// <summary>Secure attribute — true in prod, relax for plain-HTTP local dev/tests.</summary>
    public bool CookieSecure { get; set; } = true;

    /// <summary>Sliding refresh TTL, renewed on every rotation. Default 30 days.</summary>
    public TimeSpan RefreshTtl { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Absolute cap from first login; a family can't outlive this. Default 90 days.</summary>
    public TimeSpan RefreshAbsoluteTtl { get; set; } = TimeSpan.FromDays(90);
}
