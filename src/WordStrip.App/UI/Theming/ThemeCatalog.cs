using System.Windows;
using WordStrip.Core.Settings;
using Color = System.Windows.Media.Color;

namespace WordStrip.App.UI.Theming;

/// <summary>
/// The six themes, and everything that differs between them.
///
/// <para>Six rather than the seven that came before: Acrylic and Mica were one theme with two opacities,
/// and the Apple/visionOS and Fluent-Depth/Raycast pairs were each other with the contrast moved. Merging
/// them freed the budget for two that are genuinely different — an opaque editorial surface and a
/// monospaced terminal — and the catalogue now spans translucent to opaque, rounded to square, airy to
/// dense, and proportional to monospaced.</para>
///
/// <para>Each theme is authored to clear <c>SurfaceSeparation.Floor</c> over the backdrop it is drawn for,
/// so the safety net never has to rescue it. See <c>SurfaceSeparationTests</c>.</para>
/// </summary>
public static class ThemeCatalog
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>The system's own UI font, which is what a Windows component should speak in.</summary>
    private const string SystemSans = "Segoe UI Variable Text, Segoe UI, Segoe UI Emoji";

    public static IReadOnlyList<ThemeDefinition> All { get; } = new[]
    {
        FluentSurface(),
        SpatialGlass(),
        Command(),
        MaterialYou(),
        PaperInk(),
        TerminalMono(),
    };

    public static ThemeDefinition Get(BarTheme theme) =>
        All.FirstOrDefault(t => t.Id == theme) ?? All[0];

    // --- 1. Fluent Surface ---------------------------------------------------------------------------
    // The canonical Windows theme, and the default. Acrylic's environmental translucency with Mica's
    // restraint: enough tint to read as a surface, not enough to perform. Selection is the tonal block
    // Windows uses for text, with a small accent mark - not a capsule.
    private static ThemeDefinition FluentSurface() => new()
    {
        Id = BarTheme.FluentSurface,
        Name = "Fluent Surface",
        Personality = "Windows-native",
        Blur = BackdropBlur.Full,
        RadiusFactor = 1.0,
        DensityBias = 1.0,
        RhythmFactor = 1.0,
        Selection = SelectionStyle.NativeTonal,
        DividerStrength = 0.5,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.0,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xD0, 0xD4, 0xDD), SurfaceOpacity = 0.84,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.66,
            SheenStrength = 0.24, BezelStrength = 0.16,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.96,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.08,
            Text = Rgb(0x1B, 0x1E, 0x25), SelectedText = Rgb(0x0E, 0x11, 0x16),
            Indicator = Rgb(0x0F, 0x6C, 0xBD),
            ShadowOpacity = 0.18, ShadowBlur = 16, ShadowDepth = 3,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x3B, 0x3E, 0x45), SurfaceOpacity = 0.80,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.16,
            SheenStrength = 0.20, BezelStrength = 0.18,
            SelectedSurface = Rgb(0x5E, 0x63, 0x6D), SelectedOpacity = 0.95,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.22,
            Text = Rgb(0xEC, 0xEE, 0xF2), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x60, 0xB4, 0xFF),
            ShadowOpacity = 0.40, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
    };

    // --- 2. Spatial Glass ----------------------------------------------------------------------------
    // The premium light theme: milky, luminous, roomier than anything else here, with the largest radius
    // and no dividers at all - space separates the candidates. Selection is a soft capsule of the same
    // material, slightly lifted. Deliberately the one theme allowed to feel fluid.
    private static ThemeDefinition SpatialGlass() => new()
    {
        Id = BarTheme.SpatialGlass,
        Name = "Spatial Glass",
        Personality = "Light and luminous",
        Blur = BackdropBlur.Full,
        RadiusFactor = 1.35,
        DensityBias = 1.08,
        RhythmFactor = 1.3,
        Selection = SelectionStyle.SoftCapsule,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.15,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xD6, 0xDA, 0xE4), SurfaceOpacity = 0.80,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.90,
            SheenStrength = 0.34, BezelStrength = 0.22,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.92,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.85,
            Text = Rgb(0x2B, 0x2F, 0x38), SelectedText = Rgb(0x12, 0x15, 0x1B),
            Indicator = Rgb(0x4C, 0x8D, 0xFF),
            ShadowOpacity = 0.20, ShadowBlur = 30, ShadowDepth = 7,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0xE8, 0xEC, 0xF4), SurfaceOpacity = 0.46,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.45,
            SheenStrength = 0.30, BezelStrength = 0.22,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.86,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.60,
            Text = Rgb(0xF6, 0xF8, 0xFC), SelectedText = Rgb(0x16, 0x19, 0x20),
            Indicator = Rgb(0x8F, 0xBC, 0xFF),
            ShadowOpacity = 0.42, ShadowBlur = 34, ShadowDepth = 8,
            HoverBrightness = 0.07,
        },
    };

    // --- 3. Command ----------------------------------------------------------------------------------
    // Dark, matte and compact - an instrument rather than a pane of glass. Almost no translucency, a small
    // radius, tight rhythm, and a selection that reads as a raised key. The fastest of the glass-family
    // themes: short travel, quick settle, because this is the theme for people who cycle Tab all day.
    private static ThemeDefinition Command() => new()
    {
        Id = BarTheme.Command,
        Name = "Command",
        Personality = "Dark and fast",
        Blur = BackdropBlur.Subtle,
        RadiusFactor = 0.72,
        DensityBias = 0.90,
        RhythmFactor = 0.85,
        Selection = SelectionStyle.RaisedTonal,
        DividerStrength = 0.35,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.62,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0x20, 0x22, 0x26), SurfaceOpacity = 0.97,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.08,
            SelectedSurface = Rgb(0x3E, 0x42, 0x4A), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.20,
            Text = Rgb(0xCE, 0xD2, 0xD9), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x7A, 0xD1, 0xB0),
            ShadowOpacity = 0.30, ShadowBlur = 16, ShadowDepth = 3,
            HoverBrightness = 0.07,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x33, 0x35, 0x3B), SurfaceOpacity = 0.97,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.16,
            SheenStrength = 0.0, BezelStrength = 0.10,
            SelectedSurface = Rgb(0x4E, 0x52, 0x5B), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.24,
            Text = Rgb(0xD6, 0xDA, 0xE1), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x7A, 0xD1, 0xB0),
            ShadowOpacity = 0.38, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.08,
        },
    };

    // --- 4. Material You -----------------------------------------------------------------------------
    // The colour-aware one: tonal surfaces, a filled selection container, generous roundness. Its accent
    // family is derived from the Windows accent colour at startup (see AccentTint), which is the colour
    // adaptation this platform actually offers - no wallpaper sampling, no separate extraction system.
    private static ThemeDefinition MaterialYou() => new()
    {
        Id = BarTheme.MaterialYou,
        Name = "Material You",
        Personality = "Tonal and expressive",
        Blur = BackdropBlur.None,
        RadiusFactor = 1.2,
        DensityBias = 1.0,
        RhythmFactor = 1.05,
        Selection = SelectionStyle.FilledTonal,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.0,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xDA, 0xD5, 0xE6), SurfaceOpacity = 0.98,
            Border = Rgb(0x6A, 0x5A, 0x84), BorderOpacity = 0.16,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xC9, 0xBF, 0xF0), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x65, 0x50, 0xA6), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x44, 0x41, 0x4D), SelectedText = Rgb(0x21, 0x00, 0x5D),
            Indicator = Rgb(0x65, 0x50, 0xA6),
            ShadowOpacity = 0.16, ShadowBlur = 12, ShadowDepth = 3,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x36, 0x32, 0x3F), SurfaceOpacity = 0.98,
            Border = Rgb(0xD0, 0xC4, 0xF0), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x4F, 0x43, 0x78), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xD0, 0xC4, 0xF0), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xE7, 0xE1, 0xF0), SelectedText = Rgb(0xEA, 0xDD, 0xFF),
            Indicator = Rgb(0xD0, 0xBC, 0xFF),
            ShadowOpacity = 0.34, ShadowBlur = 14, ShadowDepth = 3,
            HoverBrightness = 0.06,
        },
    };

    // --- 5. Paper & Ink ------------------------------------------------------------------------------
    // No glass, no blur, no gloss: a warm opaque sheet with a thin warm rule and almost no elevation. The
    // selection is an ink underline and nothing else - editorial, not interactive. Cheap to draw, and the
    // calmest thing in the catalogue; meant for Notepad, Word and long-form writing.
    private static ThemeDefinition PaperInk() => new()
    {
        Id = BarTheme.PaperInk,
        Name = "Paper & Ink",
        Personality = "Warm and editorial",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.55,
        DensityBias = 1.0,
        RhythmFactor = 1.1,
        Selection = SelectionStyle.Underline,
        DividerStrength = 0.28,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.5,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xE8, 0xE1, 0xD2), SurfaceOpacity = 1.0,
            Border = Rgb(0x8A, 0x77, 0x5A), BorderOpacity = 0.30,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xE8, 0xE1, 0xD2), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x2A, 0x24, 0x1B), SelectedText = Rgb(0x14, 0x10, 0x0A),
            Indicator = Rgb(0x8A, 0x3B, 0x22),
            ShadowOpacity = 0.10, ShadowBlur = 10, ShadowDepth = 2,
            HoverBrightness = 0.03,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x32, 0x2C, 0x24), SurfaceOpacity = 1.0,
            Border = Rgb(0xE8, 0xD9, 0xBC), BorderOpacity = 0.22,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x32, 0x2C, 0x24), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xEC, 0xE2, 0xD2), SelectedText = Rgb(0xFF, 0xF8, 0xEC),
            Indicator = Rgb(0xE0, 0x8A, 0x5C),
            ShadowOpacity = 0.26, ShadowBlur = 12, ShadowDepth = 2,
            HoverBrightness = 0.05,
        },
    };

    // --- 6. Terminal Monospace -----------------------------------------------------------------------
    // The radical one: OLED black, monospaced, nearly square, tight, and instant. Selection is a solid
    // block with the text knocked out of it, which is what a terminal cursor is. No blur, no shadow worth
    // the name, no rounding to speak of - the silhouette alone identifies it.
    private static ThemeDefinition TerminalMono() => new()
    {
        Id = BarTheme.TerminalMono,
        Name = "Terminal",
        Personality = "Monospaced and instant",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.3,
        DensityBias = 0.86,
        RhythmFactor = 0.8,
        Selection = SelectionStyle.BlockCursor,
        DividerStrength = 0.0,
        FontFamily = "Cascadia Code, Cascadia Mono, Consolas, Courier New",
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.0,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0x0B, 0x0D, 0x0E), SurfaceOpacity = 1.0,
            Border = Rgb(0x3A, 0xE0, 0x9A), BorderOpacity = 0.30,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x3A, 0xE0, 0x9A), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x3A, 0xE0, 0x9A), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xC8, 0xD6, 0xCE), SelectedText = Rgb(0x04, 0x0A, 0x07),
            Indicator = Rgb(0x3A, 0xE0, 0x9A),
            ShadowOpacity = 0.22, ShadowBlur = 8, ShadowDepth = 1,
            HoverBrightness = 0.10,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x14, 0x17, 0x18), SurfaceOpacity = 1.0,
            Border = Rgb(0x3A, 0xE0, 0x9A), BorderOpacity = 0.34,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x3A, 0xE0, 0x9A), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x3A, 0xE0, 0x9A), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xC8, 0xD6, 0xCE), SelectedText = Rgb(0x04, 0x0A, 0x07),
            Indicator = Rgb(0x3A, 0xE0, 0x9A),
            ShadowOpacity = 0.30, ShadowBlur = 10, ShadowDepth = 1,
            HoverBrightness = 0.10,
        },
    };
}
