using ErrorOr;

namespace Iedora.Menus;

/// <summary>The Menu module's error catalog (module-private, like IdentityErrors/TenancyErrors).
/// Stable machine-readable codes; the type selects the HTTP status via ProblemResults.</summary>
internal static class MenuErrors
{
    public static readonly Error InvalidReorder = Error.Validation(
        "menu.invalid_reorder", "orderedIds must list every child under this parent exactly once.");

    public static readonly Error TooManyTags = Error.Validation(
        "menu.too_many_tags", $"An item may have at most {BuilderText.MaxTags} tags.");

    public static readonly Error TooManyVariants = Error.Validation(
        "menu.too_many_variants", $"An item may have at most {BuilderText.MaxVariants} variants.");

    public static Error InvalidPrice(string field) => Error.Validation(
        "menu.invalid_price", $"{field} must be between 0 and {BuilderText.MaxPriceCents} cents.");

    public static readonly Error InvalidSlug = Error.Validation(
        "menu.invalid_slug", "Slug must be 2-40 lowercase letters, digits or single dashes.");

    public static readonly Error SlugTaken = Error.Conflict(
        "menu.slug_taken", "That slug is already in use.");

    public static readonly Error UnsupportedCurrency = Error.Validation(
        "menu.unsupported_currency", "Unsupported currency.");

    public static readonly Error UnknownLanguage = Error.Validation(
        "menu.unknown_language", "Unknown language code.");

    public static readonly Error DefaultNotSupported = Error.Validation(
        "menu.default_language_unsupported", "The default language must be in the supported languages.");

    public static readonly Error UnknownThemeFont = Error.Validation("menu.unknown_theme_font", "Unknown theme font.");
    public static readonly Error UnknownThemeLayout = Error.Validation("menu.unknown_theme_layout", "Unknown theme layout.");
    public static readonly Error ThemeColorTooLong = Error.Validation("menu.theme_color_too_long", "Theme colour is too long.");

    // Deferred: rotating all content to a new default language (promoteDefaultLanguage) is its own slice.
    public static readonly Error DefaultLanguageChangeUnsupported = Error.Validation(
        "menu.default_language_change_unsupported", "Changing the default language isn't supported yet.");

    public static readonly Error InvalidQrCode = Error.Validation(
        "menu.invalid_qr_code", "A QR code must be 1-64 chars of a-z, 0-9, _ or -.");

    public static readonly Error UnknownRestaurant = Error.Validation(
        "menu.unknown_restaurant", "No restaurant with that id.");
}
