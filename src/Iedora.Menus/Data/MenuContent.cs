namespace Iedora.Menus;

// The menu content hierarchy: Restaurant → Menu → Category → Item, under the `menu` schema.
//
// i18n model (ported from the Bun service): the plain columns (Name, Description) hold the
// restaurant's DEFAULT language; the *_i18n jsonb maps hold non-default overrides only. Readers
// apply the requested → default → empty fallback (see the module's Localization helper). Keys are
// language codes ("en", "pt"); values are the translated strings.

/// <summary>A localized text map (language code → translation). Stored as jsonb.</summary>
public sealed class LocalizedText : Dictionary<string, string>;

/// <summary>A restaurant's public visual theme. Stored as jsonb; every field optional.</summary>
public sealed record Theme(string? PrimaryColor, string? SecondaryColor, string? Font, string? Layout);

/// <summary>One alternative price of an item ("Meia dose", "Jarra 0.5L"). Stored in the item's
/// jsonb variants array.</summary>
public sealed record Variant(string Label, LocalizedText? LabelI18n, int PriceCents);

/// <summary>The tenant-scoped root of the content hierarchy. <see cref="TenantId"/> references an
/// auth-service tenant by id only (no cross-module FK).</summary>
public sealed class Restaurant
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public LocalizedText? DescriptionI18n { get; set; }
    public string? LogoUrl { get; set; }
    public string? BannerUrl { get; set; }
    public Theme? Theme { get; set; }
    public string DefaultLanguage { get; set; } = "en";
    public List<string> SupportedLanguages { get; set; } = ["en"];
    public string DefaultCurrency { get; set; } = "EUR";
    public DateTimeOffset? OnboardingCompletedAt { get; set; } // null = mid-wizard
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A menu board within a restaurant ("Lunch", "Drinks"). Only <see cref="Active"/> menus
/// render on the public page.</summary>
public sealed class Menu
{
    public Guid Id { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public LocalizedText? NameI18n { get; set; }
    public string? Description { get; set; }
    public LocalizedText? DescriptionI18n { get; set; }
    public int Position { get; set; }
    public bool Active { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A section within a menu ("Starters", "Mains"). <see cref="RestaurantId"/> is denormalized
/// so every mutation can scope its WHERE clause by restaurant (tenancy defense-in-depth).</summary>
public sealed class Category : IOrderable
{
    public Guid Id { get; set; }
    public Guid MenuId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public LocalizedText? NameI18n { get; set; }
    public string? Description { get; set; }
    public LocalizedText? DescriptionI18n { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? TranslationsSyncedAt { get; set; } // stale iff null or < UpdatedAt
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A dish. <see cref="Available"/> false hides it from guests. Prices are integer cents.</summary>
public sealed class Item : IOrderable
{
    public Guid Id { get; set; }
    public Guid CategoryId { get; set; }
    public Guid RestaurantId { get; set; }
    public string Name { get; set; } = "";
    public LocalizedText? NameI18n { get; set; }
    public string? Description { get; set; }
    public LocalizedText? DescriptionI18n { get; set; }
    public int PriceCents { get; set; }
    public string Currency { get; set; } = "EUR";
    public string? ImageUrl { get; set; }
    public int Position { get; set; }
    public bool Available { get; set; } = true;
    public List<string> Tags { get; set; } = [];
    public List<Variant>? Variants { get; set; } // null = single price
    public DateTimeOffset? TranslationsSyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
