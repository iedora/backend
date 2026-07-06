namespace Iedora.Auth.Common;

/// <summary>Password-reset settings, bound from the "PasswordReset" config section.</summary>
public sealed class PasswordResetOptions
{
    /// <summary>Base URL of the frontend reset page; the email links to
    /// <c>{ResetUrlBase}?email=…&amp;token=…</c>.</summary>
    public string ResetUrlBase { get; set; } = "https://app.iedora.com/reset-password";
}
