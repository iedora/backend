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
}
