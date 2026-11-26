using System.Windows;
using System.Windows.Media;

namespace WinCareDesktop.Services;

/// <summary>
/// Runtime theme switcher between Flight-Deck Hybrid (obsidian bridge + titanium-white cards)
/// and Full Obsidian Dark Deck (deep-space dark instrument cards), keeping WCAG AA contrast in both.
/// </summary>
public static class ThemePaletteService
{
    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var scb = new SolidColorBrush(Color.FromRgb(r, g, b));
        scb.Freeze();
        return scb;
    }

    public static void ApplyPalette(bool darkDeck, params ResourceDictionary?[] dictionaries)
    {
        var entries = darkDeck ? DarkDeckPalette() : HybridLightPalette();
        foreach (var dict in dictionaries)
        {
            if (dict == null) continue;
            foreach (var (key, value) in entries)
            {
                dict[key] = value;
            }
        }
    }

    public static void ApplyToCurrentApp(bool darkDeck)
    {
        var app = Application.Current;
        if (app == null || !app.CheckAccess()) return;

        var targets = new List<ResourceDictionary?> { app.Resources };
        foreach (Window win in app.Windows)
        {
            if (win.CheckAccess()) targets.Add(win.Resources);
        }
        ApplyPalette(darkDeck, targets.ToArray());
    }

    private static IReadOnlyDictionary<string, object> HybridLightPalette() => new Dictionary<string, object>
    {
        ["BgBrush"] = Brush(0xF1, 0xF5, 0xF9),
        ["Surface"] = Brush(0xFF, 0xFF, 0xFF),
        ["SurfaceHover"] = Brush(0xF8, 0xFA, 0xFC),
        ["CardBorderBrush"] = Brush(0xCB, 0xD5, 0xE1),
        ["Hairline"] = Brush(0xE2, 0xE8, 0xF0),
        ["Primary"] = Brush(0x0F, 0x76, 0x6E),
        ["PrimaryButtonBg"] = Brush(0x0F, 0x76, 0x6E),
        ["PrimaryDark"] = Brush(0x11, 0x5E, 0x59),
        ["PrimaryLight"] = Brush(0xE6, 0xF4, 0xF1),
        ["TextPrimary"] = Brush(0x0F, 0x17, 0x2A),
        ["TextSecondary"] = Brush(0x47, 0x55, 0x69),
        ["TextTertiary"] = Brush(0x5B, 0x6B, 0x80),
        ["Success"] = Brush(0x04, 0x78, 0x57),
        ["Warning"] = Brush(0xB4, 0x53, 0x09),
        ["Error"] = Brush(0xDC, 0x26, 0x26),
    };

    private static IReadOnlyDictionary<string, object> DarkDeckPalette() => new Dictionary<string, object>
    {
        ["BgBrush"] = Brush(0x09, 0x0E, 0x1A),
        ["Surface"] = Brush(0x11, 0x18, 0x27),
        ["SurfaceHover"] = Brush(0x1B, 0x26, 0x3B),
        ["CardBorderBrush"] = Brush(0x24, 0x34, 0x4D),
        ["Hairline"] = Brush(0x1E, 0x29, 0x3B),
        ["Primary"] = Brush(0x2D, 0xD4, 0xBF),
        ["PrimaryButtonBg"] = Brush(0x0F, 0x76, 0x6E),
        ["PrimaryDark"] = Brush(0x0D, 0x94, 0x88),
        ["PrimaryLight"] = Brush(0x13, 0x2E, 0x35),
        ["TextPrimary"] = Brush(0xF8, 0xFA, 0xFC),
        ["TextSecondary"] = Brush(0xCB, 0xD5, 0xE1),
        ["TextTertiary"] = Brush(0x94, 0xA3, 0xB8),
        ["Success"] = Brush(0x34, 0xD3, 0x99),
        ["Warning"] = Brush(0xFB, 0xBF, 0x24),
        ["Error"] = Brush(0xF8, 0x71, 0x71),
    };
}
