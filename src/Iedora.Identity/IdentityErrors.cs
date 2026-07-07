using ErrorOr;

namespace Iedora.Identity;

/// <summary>
/// The auth error catalog. Each error's <c>code</c> (e.g. "auth.refresh_token_reuse") is a
/// stable, machine-readable discriminator the client can branch on; the description is human
/// text. The error <c>Type</c> selects the HTTP status via <see cref="ProblemResults"/>.
/// </summary>
public static class IdentityErrors
{
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Invalid email or password.");

    public static readonly Error NoRefreshToken =
        Error.Unauthorized("auth.no_refresh_token", "No refresh token was provided.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The refresh token is invalid or expired.");

    public static readonly Error RefreshTokenReuse =
        Error.Unauthorized("auth.refresh_token_reuse", "Refresh token reuse detected; the session family was revoked.");

    public static readonly Error UserGone =
        Error.Unauthorized("auth.user_gone", "The authenticated user no longer exists.");

    public static readonly Error CurrentPasswordRequired =
        Error.Validation("auth.current_password_required", "The current password is required.");

    public static readonly Error WrongCurrentPassword =
        Error.Forbidden("auth.wrong_current_password", "The current password is incorrect.");

    public static readonly Error InvalidResetToken =
        Error.Validation("auth.invalid_reset_token", "The reset link is invalid or has expired.");

    public static readonly Error EmailTaken =
        Error.Conflict("auth.email_taken", "An account with that email already exists.");
}
