using MudBlazor;

namespace Iedora.Dashboard.Theme;

/// <summary>
/// The dashboard's MudBlazor theme — a light, restrained admin palette keyed on iedora's
/// dark-red accent. A single source of truth so every component inherits the same colors instead
/// of repeating hex values (the old hand-written CSS did the latter).
/// </summary>
public static class IedoraTheme
{
    private const string Accent = "#8b0000"; // iedora dark red
    private const string Ink = "#1a1a2e";
    private const string Line = "#e5e7eb";

    public static readonly MudTheme Instance = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = Accent,
            AppbarBackground = "#ffffff",
            AppbarText = Ink,
            Background = "#f7f7f9",
            Surface = "#ffffff",
            DrawerBackground = "#ffffff",
            DrawerText = Ink,
            DrawerIcon = "#6b7280",
            TextPrimary = Ink,
            TextSecondary = "#6b7280",
            LinesDefault = Line,
            LinesInputs = Line,
            TableLines = Line,
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "8px",
            DrawerWidthLeft = "240px",
        },
    };
}
