using System.Windows;
using WordStrip.Core.Settings;
using Color = System.Windows.Media.Color;

namespace WordStrip.App.UI.Theming;

/// <summary>
/// The eight themes, and everything that differs between them.
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
        Editorial(),
        TerminalMono(),
        PrismRail(),
    };

    public static ThemeDefinition Get(BarTheme theme) =>
        All.FirstOrDefault(t => t.Id == theme) ?? All[0];

    // --- 1. Fluent Surface ---------------------------------------------------------------------------
    // The canonical Windows theme, and the default. Acrylic's environmental translucency with Mica's
    // restraint. The selection is a filled accent pill with white text - the reference build showed the
    // Windows accent doing the work, and it is also what a Windows text selection looks like. A tonal grey
    // block with a small blue mark was the more timid reading of the same idea.
    private static ThemeDefinition FluentSurface() => new()
    {
        Id = BarTheme.FluentSurface,
        Name = "Fluent Surface",
        Personality = "Windows-native",
        Blur = BackdropBlur.Full,
        RadiusFactor = 1.15,
        DensityBias = 1.0,
        RhythmFactor = 1.0,
        Selection = SelectionStyle.FilledTonal,
        DividerStrength = 0.35,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.0,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xD5, 0xDD, 0xEA), SurfaceOpacity = 0.86,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.72,
            SheenStrength = 0.26, BezelStrength = 0.16,
            SelectedSurface = Rgb(0x2F, 0x7C, 0xE0), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x18, 0x1D, 0x27), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x2F, 0x7C, 0xE0),
            ShadowOpacity = 0.18, ShadowBlur = 16, ShadowDepth = 3,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x39, 0x3F, 0x4A), SurfaceOpacity = 0.82,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.18,
            SheenStrength = 0.20, BezelStrength = 0.18,
            SelectedSurface = Rgb(0x4C, 0x9A, 0xFF), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xEC, 0xEE, 0xF2), SelectedText = Rgb(0x0A, 0x14, 0x24),
            Indicator = Rgb(0x4C, 0x9A, 0xFF),
            ShadowOpacity = 0.40, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
    };

    // --- 2. Spatial Glass ----------------------------------------------------------------------------
    // The premium light theme: a milky white panel, the largest radius here, no dividers at all, and a
    // capsule selection in soft grey rather than white. The reference had it the way round this now is -
    // white material, grey selection - which reads far better than white-on-white, and leaves the type as
    // the thing with the contrast.
    private static ThemeDefinition SpatialGlass() => new()
    {
        Id = BarTheme.SpatialGlass,
        Name = "Spatial Glass",
        Personality = "Light and luminous",
        Blur = BackdropBlur.Full,
        RadiusFactor = 1.45,
        DensityBias = 1.10,
        RhythmFactor = 1.35,
        Selection = SelectionStyle.SoftCapsule,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.15,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xDF, 0xE1, 0xE6), SurfaceOpacity = 0.84,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.95,
            SheenStrength = 0.30, BezelStrength = 0.18,
            SelectedSurface = Rgb(0x9A, 0x9E, 0xA8), SelectedOpacity = 0.55,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.55,
            Text = Rgb(0x23, 0x26, 0x2C), SelectedText = Rgb(0x0E, 0x10, 0x14),
            Indicator = Rgb(0x6E, 0x74, 0x80),
            ShadowOpacity = 0.22, ShadowBlur = 32, ShadowDepth = 7,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0xEC, 0xEE, 0xF3), SurfaceOpacity = 0.50,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.50,
            SheenStrength = 0.28, BezelStrength = 0.20,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.72,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.55,
            Text = Rgb(0xF7, 0xF9, 0xFC), SelectedText = Rgb(0x16, 0x19, 0x20),
            Indicator = Rgb(0xC3, 0xC9, 0xD4),
            ShadowOpacity = 0.44, ShadowBlur = 36, ShadowDepth = 8,
            HoverBrightness = 0.07,
        },
    };

    // --- 3. Command ----------------------------------------------------------------------------------
    // Rebuilt to the Raycast Compact reference: a deep near-black slab with a generous radius for how
    // compact it is, a selected candidate raised a shade out of the surface, and a short coral rule under
    // it. Dense, quiet, and the fastest of the surfaced themes - this is the theme for people who cycle
    // Tab all day.
    private static ThemeDefinition Command() => new()
    {
        Id = BarTheme.Command,
        Name = "Command",
        Personality = "Dark and immediate",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.95,
        DensityBias = 0.88,
        RhythmFactor = 0.9,
        Selection = SelectionStyle.RaisedTonal,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.62,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0x17, 0x1A, 0x21), SurfaceOpacity = 0.98,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.10,
            SheenStrength = 0.0, BezelStrength = 0.06,
            SelectedSurface = Rgb(0x2B, 0x30, 0x3A), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.10,
            Text = Rgb(0xC7, 0xCC, 0xD6), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xFF, 0x4D, 0x4D),
            ShadowOpacity = 0.32, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x23, 0x27, 0x2F), SurfaceOpacity = 0.98,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.12,
            SheenStrength = 0.0, BezelStrength = 0.08,
            SelectedSurface = Rgb(0x39, 0x3F, 0x4A), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.14,
            Text = Rgb(0xCF, 0xD4, 0xDE), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xFF, 0x5C, 0x5C),
            ShadowOpacity = 0.40, ShadowBlur = 20, ShadowDepth = 5,
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
    // Warm, soft and unruled: a cream sheet with a whisper of elevation, candidates separated by space
    // alone, and a rust underline that reads as a pencil mark. Where Editorial is a ruled column, this is
    // a page - the pair are deliberately opposite sides of the same craft.
    private static ThemeDefinition PaperInk() => new()
    {
        Id = BarTheme.PaperInk,
        Name = "Paper & Ink",
        Personality = "Warm and soft",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.8,
        DensityBias = 1.04,
        RhythmFactor = 1.25,
        Selection = SelectionStyle.Underline,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.8,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xEA, 0xDF, 0xCB), SurfaceOpacity = 1.0,
            Border = Rgb(0x8A, 0x77, 0x5A), BorderOpacity = 0.18,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xEA, 0xDF, 0xCB), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x33, 0x2C, 0x21), SelectedText = Rgb(0x1A, 0x14, 0x0C),
            Indicator = Rgb(0xA8, 0x4B, 0x2A),
            ShadowOpacity = 0.14, ShadowBlur = 14, ShadowDepth = 3,
            HoverBrightness = 0.03,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x3A, 0x33, 0x29), SurfaceOpacity = 1.0,
            Border = Rgb(0xE8, 0xD9, 0xBC), BorderOpacity = 0.16,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x3A, 0x33, 0x29), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xEF, 0xE4, 0xD2), SelectedText = Rgb(0xFF, 0xF8, 0xEC),
            Indicator = Rgb(0xE8, 0x93, 0x64),
            ShadowOpacity = 0.28, ShadowBlur = 16, ShadowDepth = 3,
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
        SlotWidthFactor = 1.2,
        FontFamily = "Cascadia Code, Cascadia Mono, Consolas, Courier New, Segoe UI Emoji",
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

    // --- 7. Prism Rail -------------------------------------------------------------------------------
    // The open one. There is no panel: the candidates sit on the page, and the only drawn thing is a
    // hairline rail beneath them, lit from its left end to a dot under the selected word. The lit length
    // says where in the list the selection is, which no other theme here tells you, and it is why this
    // theme can afford to have no surface: the rail is the whole interface.
    //
    // The surface is not quite nothing - a barely-there wash keeps the words legible over a photograph or
    // a saturated page, and the separation floor will lift it further if the backdrop demands.
    private static ThemeDefinition PrismRail() => new()
    {
        Id = BarTheme.PrismRail,
        Name = "Prism Rail",
        Personality = "Open and kinetic",
        Blur = BackdropBlur.Subtle,
        RadiusFactor = 0.7,
        DensityBias = 1.06,
        RhythmFactor = 1.2,
        Selection = SelectionStyle.RailDot,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.Bold,
        MotionFactor = 1.25,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xEE, 0xF0, 0xF4), SurfaceOpacity = 0.38,
            Border = Rgb(0x2B, 0x2F, 0x38), BorderOpacity = 0.06,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x1B, 0x1E, 0x24), SelectedText = Rgb(0x0B, 0x0D, 0x11),
            Indicator = Rgb(0xF0, 0x40, 0x88),
            RailFrom = Rgb(0x3B, 0xC9, 0xF0), RailTo = Rgb(0xF0, 0x40, 0x88),
            ShadowOpacity = 0.10, ShadowBlur = 18, ShadowDepth = 3,
            HoverBrightness = 0.04,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x24, 0x27, 0x2D), SurfaceOpacity = 0.42,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.08,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xF2, 0xF4, 0xF8), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xFF, 0x5C, 0xA0),
            RailFrom = Rgb(0x5A, 0xDC, 0xFF), RailTo = Rgb(0xFF, 0x5C, 0xA0),
            ShadowOpacity = 0.28, ShadowBlur = 22, ShadowDepth = 4,
            HoverBrightness = 0.06,
        },
    };

    // --- 8. Editorial --------------------------------------------------------------------------------
    // A ruled column on ivory: a thin dark border all the way round, a rule between every candidate, square
    // corners, and a heavy ink underline under the selected word. Flat by conviction - no blur, no sheen,
    // no elevation worth the name. Paper & Ink is the warm unruled page; this is the set column.
    private static ThemeDefinition Editorial() => new()
    {
        Id = BarTheme.Editorial,
        Name = "Editorial",
        Personality = "Ruled and typographic",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.22,
        DensityBias = 0.96,
        RhythmFactor = 1.0,
        Selection = SelectionStyle.Underline,
        DividerStrength = 1.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.Medium,
        MotionFactor = 0.45,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xDD, 0xDA, 0xCF), SurfaceOpacity = 1.0,
            Border = Rgb(0x1C, 0x1C, 0x1A), BorderOpacity = 0.85,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xDD, 0xDA, 0xCF), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x14, 0x14, 0x12), SelectedText = Rgb(0x00, 0x00, 0x00),
            Indicator = Rgb(0x11, 0x11, 0x10),
            ShadowOpacity = 0.06, ShadowBlur = 6, ShadowDepth = 1,
            HoverBrightness = 0.03,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x26, 0x26, 0x23), SurfaceOpacity = 1.0,
            Border = Rgb(0xEC, 0xE7, 0xD8), BorderOpacity = 0.70,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x26, 0x26, 0x23), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xF0, 0xEC, 0xE0), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xF4, 0xF1, 0xE6),
            ShadowOpacity = 0.20, ShadowBlur = 8, ShadowDepth = 1,
            HoverBrightness = 0.05,
        },
    };
}
