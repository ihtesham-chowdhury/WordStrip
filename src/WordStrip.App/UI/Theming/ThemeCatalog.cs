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
            Surface = Rgb(0xD1, 0xD9, 0xE7), SurfaceOpacity = 0.86,
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
    // The luminous one, and the closest thing here to the glass Apple's platforms use: a bright frosted
    // body with a specular top edge, a lensed rim, the largest radius in the catalogue, no dividers, and a
    // wide soft shadow that lifts it off the page.
    //
    // Its dark variant used to be a pale veil with dark text, which over a dark application looked like a
    // sheet of tracing paper someone had dropped on the screen. Dark glass is *dark*: a deep translucent
    // body, light text, and a selection that is a brighter piece of the same material rather than a white
    // slab. That is the version that survives a dark editor.
    private static ThemeDefinition SpatialGlass() => new()
    {
        Id = BarTheme.SpatialGlass,
        Name = "Spatial Glass",
        Personality = "Luminous and spatial",
        Blur = BackdropBlur.Full,
        RadiusFactor = 1.6,
        DensityBias = 1.12,
        RhythmFactor = 1.4,
        Selection = SelectionStyle.SoftCapsule,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 1.2,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xD3, 0xD8, 0xE3), SurfaceOpacity = 0.86,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.95,
            SheenStrength = 0.48, BezelStrength = 0.34,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.80,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.95,
            Text = Rgb(0x21, 0x24, 0x2B), SelectedText = Rgb(0x0C, 0x0E, 0x13),
            Indicator = Rgb(0x4C, 0x8D, 0xFF),
            ShadowOpacity = 0.24, ShadowBlur = 36, ShadowDepth = 9,
            HoverBrightness = 0.06,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x41, 0x47, 0x54), SurfaceOpacity = 0.70,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.38,
            SheenStrength = 0.40, BezelStrength = 0.34,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.22,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.50,
            Text = Rgb(0xF4, 0xF6, 0xFB), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x8F, 0xBC, 0xFF),
            ShadowOpacity = 0.46, ShadowBlur = 38, ShadowDepth = 10,
            HoverBrightness = 0.09,
        },
    };

    // --- 3. Command ----------------------------------------------------------------------------------
    // The dense instrument, built to the Raycast Compact reference: a slab, the selected word raised a
    // shade out of it, and a short coral rule beneath. Compact, quiet, and the fastest of the surfaced
    // themes - this is the theme for people who cycle Tab all day.
    //
    // Both variants are instruments rather than glass; what changes is which way up. Over a dark editor it
    // is the near-black panel it has always been. Over a white page it is the same design in daylight -
    // a cool grey slab with a white raised key - because a black bar over a white document is a hole in
    // the page, however handsome it looks on its own.
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
            Surface = Rgb(0xD6, 0xD9, 0xE0), SurfaceOpacity = 0.97,
            Border = Rgb(0x1A, 0x1D, 0x23), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.05,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x1A, 0x1D, 0x23), SelectedBorderOpacity = 0.10,
            Text = Rgb(0x26, 0x2A, 0x33), SelectedText = Rgb(0x10, 0x12, 0x17),
            Indicator = Rgb(0xE0, 0x43, 0x3C),
            ShadowOpacity = 0.20, ShadowBlur = 14, ShadowDepth = 3,
            HoverBrightness = 0.04,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x2C, 0x31, 0x3A), SurfaceOpacity = 0.98,
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
    // alone, a rounded silhouette and a rust mark that reads as a pencil line. Editorial is the ruled
    // column on cool stock; this is the warm page.
    private static ThemeDefinition PaperInk() => new()
    {
        Id = BarTheme.PaperInk,
        Name = "Paper & Ink",
        Personality = "Warm and soft",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.9,
        DensityBias = 1.06,
        RhythmFactor = 1.3,
        Selection = SelectionStyle.Underline,
        DividerStrength = 0.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.SemiBold,
        MotionFactor = 0.8,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xEA, 0xDC, 0xC2), SurfaceOpacity = 1.0,
            Border = Rgb(0x8A, 0x77, 0x5A), BorderOpacity = 0.16,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xEA, 0xDC, 0xC2), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x3A, 0x30, 0x22), SelectedText = Rgb(0x1E, 0x17, 0x0D),
            Indicator = Rgb(0xB5, 0x50, 0x2C),
            ShadowOpacity = 0.16, ShadowBlur = 16, ShadowDepth = 3,
            HoverBrightness = 0.03,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x43, 0x39, 0x2C), SurfaceOpacity = 1.0,
            Border = Rgb(0xE8, 0xD9, 0xBC), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x43, 0x39, 0x2C), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xF2, 0xE6, 0xD2), SelectedText = Rgb(0xFF, 0xF8, 0xEC),
            Indicator = Rgb(0xEE, 0x9A, 0x66),
            ShadowOpacity = 0.30, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.05,
        },
    };

    // --- 6. Terminal Monospace -----------------------------------------------------------------------
    // The radical one: monospaced, nearly square, tight, and instant. Selection is a solid block with the
    // text knocked out of it, which is what a terminal cursor is. No blur, no shadow worth the name, no
    // rounding to speak of - the silhouette alone identifies it.
    //
    // Over a dark application it is the OLED black a terminal actually is. Over a white page that same
    // black reads as a hole cut in the document, so the light variant is graphite: unmistakably a terminal,
    // without the hard edge of pure black against paper.
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
            Surface = Rgb(0x26, 0x2B, 0x2E), SurfaceOpacity = 0.98,
            Border = Rgb(0x35, 0xC9, 0x8C), BorderOpacity = 0.22,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x35, 0xC9, 0x8C), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x35, 0xC9, 0x8C), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xCD, 0xD8, 0xD2), SelectedText = Rgb(0x08, 0x12, 0x0C),
            Indicator = Rgb(0x35, 0xC9, 0x8C),
            ShadowOpacity = 0.18, ShadowBlur = 10, ShadowDepth = 2,
            HoverBrightness = 0.09,
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
    // A ruled column: a dark keyline all the way round, a rule between every candidate, square corners and
    // a heavy ink underline. Cool paper stock rather than Paper & Ink's warm cream, lighter type, tighter
    // rhythm - the two share a craft and nothing else.
    //
    // The dark variant is a night edition of the same column, not an inverted one: deep ink, warm ivory
    // type, ivory rules, ivory underline.
    private static ThemeDefinition Editorial() => new()
    {
        Id = BarTheme.Editorial,
        Name = "Editorial",
        Personality = "Ruled and typographic",
        Blur = BackdropBlur.None,
        RadiusFactor = 0.18,
        DensityBias = 0.96,
        RhythmFactor = 0.95,
        Selection = SelectionStyle.Underline,
        DividerStrength = 1.0,
        FontFamily = SystemSans,
        PrimaryWeight = FontWeights.Medium,
        MotionFactor = 0.45,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xDD, 0xDC, 0xD4), SurfaceOpacity = 1.0,
            Border = Rgb(0x14, 0x14, 0x16), BorderOpacity = 0.90,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xE2, 0xE1, 0xDA), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0x1A, 0x1A, 0x1C), SelectedText = Rgb(0x00, 0x00, 0x00),
            Indicator = Rgb(0x0E, 0x0E, 0x10),
            ShadowOpacity = 0.05, ShadowBlur = 5, ShadowDepth = 1,
            HoverBrightness = 0.03,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x30, 0x30, 0x33), SurfaceOpacity = 1.0,
            Border = Rgb(0xF2, 0xEC, 0xDC), BorderOpacity = 0.75,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x30, 0x30, 0x33), SelectedOpacity = 0.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.0,
            Text = Rgb(0xF2, 0xEC, 0xDC), SelectedText = Rgb(0xFF, 0xFC, 0xF4),
            Indicator = Rgb(0xF6, 0xF1, 0xE4),
            ShadowOpacity = 0.22, ShadowBlur = 8, ShadowDepth = 1,
            HoverBrightness = 0.05,
        },
    };
}
