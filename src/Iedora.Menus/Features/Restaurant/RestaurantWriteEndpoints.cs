using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using ErrorOr;
using Framework.Web;
using Microsoft.EntityFrameworkCore;

namespace Iedora.Menus;

public sealed record ThemeInput(string? PrimaryColor, string? SecondaryColor, string? Font, string? Layout);

// The full editable identity (a settings-form replace, not a sparse patch): every editable field is
// sent each save. Optional fields (description, i18n, theme) may be null to clear.
public sealed record UpdateIdentityRequest(
    [property: Required, StringLength(80, MinimumLength = 1)] string Name,
    [property: StringLength(1000)] string? Description,
    Dictionary<string, string>? DescriptionI18n,
    ThemeInput? Theme,
    [property: Required] string DefaultLanguage,
    [property: Required, MinLength(1)] IReadOnlyList<string> SupportedLanguages,
    [property: Required] string DefaultCurrency,
    // IANA timezone for analytics bucketing; null leaves the current value unchanged.
    string? TimeZone = null);

public sealed record RenameSlugRequest([property: Required] string Slug);

// Restaurant-level writes under /api/restaurants/{slug} (owner/staff, scoped, synchronous).
public static partial class RestaurantWriteEndpoints
{
    private static readonly string[] Currencies = ["EUR", "USD", "GBP", "BRL", "CHF", "CAD", "AUD", "JPY"];
    private static readonly string[] ThemeFonts = ["inter", "playfair", "lora", "space-grotesk"];
    private static readonly string[] ThemeLayouts = ["classic", "minimal", "editorial", "cards"];

    // 2-40 chars, lowercase alphanumerics + single dashes, starting/ending alphanumeric.
    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public static void MapRestaurantWrites(this RouteGroupBuilder scoped)
    {
        // PATCH /api/restaurants/{slug} — replace the restaurant's identity; returns the fresh detail.
        scoped.MapPatch("/", async (
                string slug, UpdateIdentityRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadTrackedAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            // Changing the default language needs a content rotation (promoteDefaultLanguage) — deferred.
            if (req.DefaultLanguage != rest.DefaultLanguage)
                return ProblemResults.From(MenuErrors.DefaultLanguageChangeUnsupported);

            var languages = ValidateLanguages(req.DefaultLanguage, req.SupportedLanguages);
            if (languages.IsError) return ProblemResults.From(languages.Errors);
            var currency = NormalizeCurrency(req.DefaultCurrency);
            if (currency.IsError) return ProblemResults.From(currency.Errors);
            var name = BuilderText.Name(req.Name, BuilderText.MaxShortName, "name");
            if (name.IsError) return ProblemResults.From(name.Errors);
            var description = BuilderText.Optional(req.Description, BuilderText.MaxDescription, "description");
            if (description.IsError) return ProblemResults.From(description.Errors);
            var descriptionI18n = BuilderText.I18n(req.DescriptionI18n, req.DefaultLanguage, "description");
            if (descriptionI18n.IsError) return ProblemResults.From(descriptionI18n.Errors);
            var theme = ValidateTheme(req.Theme);
            if (theme.IsError) return ProblemResults.From(theme.Errors);
            if (req.TimeZone is not null && !Timezones.IsValid(req.TimeZone))
                return ProblemResults.From(MenuErrors.UnknownTimeZone);

            rest.Name = name.Value;
            rest.Description = description.Value;
            rest.DescriptionI18n = descriptionI18n.Value;
            rest.Theme = theme.Value;
            rest.SupportedLanguages = [.. req.SupportedLanguages];
            rest.DefaultCurrency = currency.Value;
            if (req.TimeZone is not null) rest.TimeZone = req.TimeZone;
            rest.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.Ok(RestaurantDetail.From(rest));
        })
        .WithName("UpdateRestaurant").WithSummary("Update a restaurant's identity (owner/staff).")
        .Produces<RestaurantDetail>(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound);

        // POST /api/restaurants/{slug}/slug — rename the (globally unique) slug.
        scoped.MapPost("/slug", async (
                string slug, RenameSlugRequest req, ClaimsPrincipal user,
                MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadTrackedAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            var next = (req.Slug ?? "").Trim();
            if (!IsValidSlug(next)) return ProblemResults.From(MenuErrors.InvalidSlug);
            if (next != rest.Slug && await db.Restaurants.AnyAsync(r => r.Slug == next, ct))
                return ProblemResults.From(MenuErrors.SlugTaken);

            rest.Slug = next;
            rest.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("RenameSlug").WithSummary("Rename a restaurant's public slug (owner/staff).")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest).ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict);

        // POST /api/restaurants/{slug}/complete-onboarding — mark the setup wizard done (idempotent).
        scoped.MapPost("/complete-onboarding", async (
                string slug, ClaimsPrincipal user, MenuDbContext db, TimeProvider clock, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadTrackedAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            rest.OnboardingCompletedAt ??= clock.GetUtcNow();
            rest.UpdatedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("CompleteOnboarding").WithSummary("Mark a restaurant's onboarding complete (owner/staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);

        // DELETE /api/restaurants/{slug} — delete the restaurant and all its content (cascade).
        scoped.MapDelete("/", async (
                string slug, ClaimsPrincipal user, MenuDbContext db, CancellationToken ct) =>
        {
            var rest = await RestaurantAccess.LoadAsync(db, user, slug, ct);
            if (rest is null) return Results.NotFound();

            await db.Restaurants.Where(r => r.Id == rest.Id).ExecuteDeleteAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteRestaurant").WithSummary("Delete a restaurant and all its content (owner/staff).")
        .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static bool IsValidSlug(string s) =>
        s.Length is >= 2 and <= 40 && !s.Contains("--") && SlugPattern().IsMatch(s);

    private static ErrorOr<Theme?> ValidateTheme(ThemeInput? t)
    {
        if (t is null) return (Theme?)null;
        if (t.Font is { } font && !ThemeFonts.Contains(font)) return MenuErrors.UnknownThemeFont;
        if (t.Layout is { } layout && !ThemeLayouts.Contains(layout)) return MenuErrors.UnknownThemeLayout;
        if ((t.PrimaryColor?.Length ?? 0) > 32 || (t.SecondaryColor?.Length ?? 0) > 32) return MenuErrors.ThemeColorTooLong;
        return new Theme(t.PrimaryColor, t.SecondaryColor, t.Font, t.Layout);
    }

    private static ErrorOr<Success> ValidateLanguages(string defaultLang, IReadOnlyList<string> supported)
    {
        if (!Localization.Languages.Contains(defaultLang)) return MenuErrors.UnknownLanguage;
        foreach (var code in supported)
            if (!Localization.Languages.Contains(code)) return MenuErrors.UnknownLanguage;
        return supported.Contains(defaultLang) ? Result.Success : MenuErrors.DefaultNotSupported;
    }

    private static ErrorOr<string> NormalizeCurrency(string currency)
    {
        var code = (currency ?? "").Trim().ToUpperInvariant();
        return Currencies.Contains(code) ? code : MenuErrors.UnsupportedCurrency;
    }
}
