using ErrorOr;
using Iedora.Data;

namespace Iedora.Menus;

/// <summary>Shared normalization + limits for builder writes (ported from the Bun service's
/// validate.ts). Names are trimmed; i18n maps are pruned to non-default overrides only.</summary>
internal static class BuilderText
{
    public const int MaxShortName = 80;   // menu + category names
    public const int MaxItemName = 120;   // item names + variant labels
    public const int MaxDescription = 1000;
    public const int MaxPriceCents = 100_000_00; // €1,000.00
    public const int MaxVariants = 20;
    public const int MaxTags = 20;

    /// <summary>Trim a required name; validation error if empty or over the limit.</summary>
    public static ErrorOr<string> Name(string value, int limit, string field)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length == 0) return Error.Validation($"menu.{field}_required", $"{field} is required.");
        if (trimmed.Length > limit) return Error.Validation($"menu.{field}_too_long", $"{field} must be at most {limit} characters.");
        return trimmed;
    }

    /// <summary>Trim an optional text field; empty → null (stored NULL). Validation error if over limit.</summary>
    public static ErrorOr<string?> Optional(string? value, int limit, string field)
    {
        var trimmed = (value ?? "").Trim();
        if (trimmed.Length > limit) return Error.Validation($"menu.{field}_too_long", $"{field} must be at most {limit} characters.");
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>
    /// Prune an i18n map to overrides only: trim values, drop empties, unknown locales, and the
    /// default-language key (that value lives in the plain column, so an override would shadow it).
    /// Returns null when nothing remains. Validation error if any value exceeds the length bound.
    /// </summary>
    public static ErrorOr<LocalizedText?> I18n(Dictionary<string, string>? map, string defaultLang, string field)
    {
        if (map is null) return (LocalizedText?)null;
        var pruned = new LocalizedText();
        foreach (var (code, raw) in map)
        {
            var v = (raw ?? "").Trim();
            if (v.Length > MaxDescription)
                return Error.Validation($"menu.{field}_i18n_too_long", $"{field} translation \"{code}\" is too long.");
            if (v.Length == 0 || !Localization.Languages.Contains(code) || code == defaultLang) continue;
            pruned[code] = v;
        }
        return pruned.Count == 0 ? (LocalizedText?)null : pruned;
    }

    /// <summary>The normalized text fields shared by menus and categories.</summary>
    public sealed record TextFields(string Name, string? Description, LocalizedText? NameI18n, LocalizedText? DescriptionI18n);

    /// <summary>Normalize a menu/category's name + description + their i18n overrides in one shot,
    /// short-circuiting on the first validation error.</summary>
    public static ErrorOr<TextFields> Text(
        string name, string? description, Dictionary<string, string>? nameI18n,
        Dictionary<string, string>? descriptionI18n, string defaultLang)
    {
        var n = Name(name, MaxShortName, "name");
        if (n.IsError) return n.Errors;
        var d = Optional(description, MaxDescription, "description");
        if (d.IsError) return d.Errors;
        var ni = I18n(nameI18n, defaultLang, "name");
        if (ni.IsError) return ni.Errors;
        var di = I18n(descriptionI18n, defaultLang, "description");
        if (di.IsError) return di.Errors;
        return new TextFields(n.Value, d.Value, ni.Value, di.Value);
    }
}
