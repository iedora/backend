
namespace Iedora.Menus;

/// <summary>
/// Localization for the menu content model (ported from the Bun service's i18n.ts). The i18n model
/// stores the restaurant's DEFAULT language in the plain columns and non-default overrides in the
/// <c>*_i18n</c> jsonb maps; every localized field resolves requested → default → empty.
/// </summary>
public static class Localization
{
    /// <summary>The locales the product can serve.</summary>
    public static readonly string[] Languages = ["en", "pt", "es", "fr"];

    /// <summary>Resolve one field for a language: override → plain (default language) → the plain
    /// value. The single fallback rule used by every localized field.</summary>
    public static string Pick(string? plain, LocalizedText? i18n, string lang) =>
        i18n is not null && i18n.TryGetValue(lang, out var v) ? v : plain ?? "";

    /// <summary>Negotiate the response language: an explicit <c>?lang=</c> wins (if supported), then
    /// the <c>Accept-Language</c> header's first supported base tag, then the restaurant default.</summary>
    public static string PickLanguage(string param, string acceptHeader, IReadOnlyList<string> supported, string fallback)
    {
        if (supported.Contains(param)) return param;
        foreach (var part in acceptHeader.Split(','))
        {
            var tag = part.Trim().Split(';')[0];
            var baseTag = tag.Split('-')[0]; // "pt-BR" → "pt"
            if (supported.Contains(baseTag)) return baseTag;
        }
        return fallback;
    }
}
