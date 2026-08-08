using WordStrip.Core.Settings;
using Color = System.Windows.Media.Color;

namespace WordStrip.App.UI.Theming;

/// <summary>
/// The seven visual presets. Everything that differs between themes lives here and nowhere else — the
/// component, geometry, interaction and motion are identical across all of them.
///
/// <para>Each theme is authored twice, for bright and for dark backdrops, because this strip floats over
/// whatever application the user happens to be typing in. A theme that only works over white is not
/// finished.</para>
/// </summary>
public static class ThemeCatalog
{
    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    public static IReadOnlyList<ThemeDefinition> All { get; } = new[]
    {
        FluentAcrylic(),
        MicaInspired(),
        FluentDepth(),
        AppleFrosted(),
        RaycastFloating(),
        VisionInspired(),
        Material3(),
    };

    public static ThemeDefinition Get(BarTheme theme) =>
        All.FirstOrDefault(t => t.Id == theme) ?? All[0];

    // --- 1. Fluent Acrylic ---------------------------------------------------------------------------
    // A premium Windows 11 floating utility: translucent and environmental, but legible over plain white.
    private static ThemeDefinition FluentAcrylic() => new()
    {
        Id = BarTheme.FluentAcrylic,
        Name = "Fluent Acrylic",
        Description = "Translucent Windows 11 utility surface. The most native-feeling option.",
        Blur = BackdropBlur.Full,
        CornerRadius = 14,
        ShowIndicator = true,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xF3, 0xF4, 0xF7), SurfaceOpacity = 0.82,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.70,
            SheenStrength = 0.35, BezelStrength = 0.22,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.98,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.10,
            Text = Rgb(0x2B, 0x2F, 0x38), SelectedText = Rgb(0x10, 0x13, 0x18),
            Indicator = Rgb(0x0F, 0x6C, 0xBD),
            ShadowOpacity = 0.20, ShadowBlur = 16, ShadowDepth = 3,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x2C, 0x2E, 0x33), SurfaceOpacity = 0.80,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.16,
            SheenStrength = 0.30, BezelStrength = 0.24,
            SelectedSurface = Rgb(0x5A, 0x5E, 0x68), SelectedOpacity = 0.95,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.24,
            Text = Rgb(0xEC, 0xEE, 0xF2), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x60, 0xB4, 0xFF),
            ShadowOpacity = 0.42, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
    };

    // --- 2. Mica-inspired ----------------------------------------------------------------------------
    // Calmer and more opaque, with the blur pulled back. Reads as part of the desktop rather than over it.
    private static ThemeDefinition MicaInspired() => new()
    {
        Id = BarTheme.MicaInspired,
        Name = "Mica-inspired",
        Description = "Calm, mostly opaque and quiet. Very little blur.",
        Blur = BackdropBlur.Subtle,
        CornerRadius = 13,
        ShowIndicator = true,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xF7, 0xF7, 0xF9), SurfaceOpacity = 0.94,
            Border = Rgb(0x1B, 0x1D, 0x22), BorderOpacity = 0.10,
            SheenStrength = 0.10, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x1B, 0x1D, 0x22), SelectedBorderOpacity = 0.14,
            Text = Rgb(0x33, 0x36, 0x3D), SelectedText = Rgb(0x14, 0x16, 0x1A),
            Indicator = Rgb(0x0F, 0x6C, 0xBD),
            ShadowOpacity = 0.14, ShadowBlur = 12, ShadowDepth = 2,
            HoverBrightness = 0.04,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x25, 0x27, 0x2B), SurfaceOpacity = 0.94,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.12,
            SheenStrength = 0.08, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x44, 0x47, 0x4E), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.18,
            Text = Rgb(0xE4, 0xE6, 0xEA), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0x60, 0xB4, 0xFF),
            ShadowOpacity = 0.34, ShadowBlur = 14, ShadowDepth = 3,
            HoverBrightness = 0.06,
        },
    };

    // --- 3. Fluent 2 + Acrylic + Depth ---------------------------------------------------------------
    // The expressive Fluent: darker plate, stronger elevation, selection clearly lifted off the surface.
    private static ThemeDefinition FluentDepth() => new()
    {
        Id = BarTheme.FluentDepth,
        Name = "Fluent 2 + Depth",
        Description = "Deeper, more dimensional Fluent with a clearly elevated selection.",
        Blur = BackdropBlur.Full,
        CornerRadius = 15,
        ShowIndicator = true,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0x3A, 0x3F, 0x49), SurfaceOpacity = 0.90,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.26,
            SheenStrength = 0.45, BezelStrength = 0.38,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.97,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.55,
            Text = Rgb(0xE9, 0xEC, 0xF1), SelectedText = Rgb(0x14, 0x17, 0x1D),
            Indicator = Rgb(0x4C, 0xA0, 0xFF),
            ShadowOpacity = 0.38, ShadowBlur = 22, ShadowDepth = 5,
            HoverBrightness = 0.08,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x33, 0x38, 0x42), SurfaceOpacity = 0.88,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.22,
            SheenStrength = 0.40, BezelStrength = 0.36,
            SelectedSurface = Rgb(0xF2, 0xF5, 0xFA), SelectedOpacity = 0.96,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.50,
            Text = Rgb(0xE9, 0xEC, 0xF1), SelectedText = Rgb(0x14, 0x17, 0x1D),
            Indicator = Rgb(0x60, 0xB4, 0xFF),
            ShadowOpacity = 0.48, ShadowBlur = 24, ShadowDepth = 6,
            HoverBrightness = 0.08,
        },
    };

    // --- 4. Apple-style frosted ----------------------------------------------------------------------
    // Bright, airy and typography-led. Restraint rather than optical simulation.
    private static ThemeDefinition AppleFrosted() => new()
    {
        Id = BarTheme.AppleFrosted,
        Name = "Apple Frosted",
        Description = "Bright, minimal and typography-led. Soft translucency, no heavy effects.",
        Blur = BackdropBlur.Full,
        CornerRadius = 16,
        ShowIndicator = false,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xFB, 0xFB, 0xFD), SurfaceOpacity = 0.78,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.85,
            SheenStrength = 0.28, BezelStrength = 0.16,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x00, 0x00, 0x00), SelectedBorderOpacity = 0.08,
            Text = Rgb(0x3C, 0x3C, 0x43), SelectedText = Rgb(0x00, 0x00, 0x00),
            Indicator = Rgb(0x00, 0x7A, 0xFF),
            ShadowOpacity = 0.16, ShadowBlur = 20, ShadowDepth = 4,
            HoverBrightness = 0.04,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x3A, 0x3A, 0x3E), SurfaceOpacity = 0.74,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.22,
            SheenStrength = 0.26, BezelStrength = 0.18,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.96,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.30,
            Text = Rgb(0xF2, 0xF2, 0xF7), SelectedText = Rgb(0x00, 0x00, 0x00),
            Indicator = Rgb(0x0A, 0x84, 0xFF),
            ShadowOpacity = 0.40, ShadowBlur = 24, ShadowDepth = 5,
            HoverBrightness = 0.06,
        },
    };

    // --- 5. Raycast-style floating -------------------------------------------------------------------
    // Dark, dense and high contrast. Selection is a lighter surface, not a bright white slab.
    private static ThemeDefinition RaycastFloating() => new()
    {
        Id = BarTheme.RaycastFloating,
        Name = "Raycast Floating",
        Description = "Dark, dense and fast. Built for power users; the words do the talking.",
        Blur = BackdropBlur.None,
        CornerRadius = 12,
        ShowIndicator = true,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0x1A, 0x1B, 0x1E), SurfaceOpacity = 0.96,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.12,
            SheenStrength = 0.0, BezelStrength = 0.10,
            SelectedSurface = Rgb(0x3A, 0x3C, 0x42), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.16,
            Text = Rgb(0xC9, 0xCC, 0xD3), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xFF, 0x63, 0x63),
            ShadowOpacity = 0.34, ShadowBlur = 18, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x16, 0x17, 0x1A), SurfaceOpacity = 0.97,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.12,
            SelectedSurface = Rgb(0x3E, 0x41, 0x48), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.20,
            Text = Rgb(0xCF, 0xD2, 0xD9), SelectedText = Rgb(0xFF, 0xFF, 0xFF),
            Indicator = Rgb(0xFF, 0x63, 0x63),
            ShadowOpacity = 0.46, ShadowBlur = 20, ShadowDepth = 5,
            HoverBrightness = 0.08,
        },
    };

    // --- 6. visionOS-inspired ------------------------------------------------------------------------
    // Pale, soft and spatial. The point is separation from the app beneath, not optical realism.
    private static ThemeDefinition VisionInspired() => new()
    {
        Id = BarTheme.VisionInspired,
        Name = "visionOS-inspired",
        Description = "Pale and spatial, with a generous soft shadow so it floats above the app.",
        Blur = BackdropBlur.Full,
        CornerRadius = 18,
        ShowIndicator = false,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xFF, 0xFF, 0xFF), SurfaceOpacity = 0.62,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.90,
            SheenStrength = 0.40, BezelStrength = 0.30,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.94,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.95,
            Text = Rgb(0x2E, 0x31, 0x38), SelectedText = Rgb(0x11, 0x13, 0x18),
            Indicator = Rgb(0x4C, 0x8D, 0xFF),
            ShadowOpacity = 0.26, ShadowBlur = 34, ShadowDepth = 8,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0xE8, 0xEC, 0xF4), SurfaceOpacity = 0.26,
            Border = Rgb(0xFF, 0xFF, 0xFF), BorderOpacity = 0.42,
            SheenStrength = 0.38, BezelStrength = 0.30,
            SelectedSurface = Rgb(0xFF, 0xFF, 0xFF), SelectedOpacity = 0.90,
            SelectedBorder = Rgb(0xFF, 0xFF, 0xFF), SelectedBorderOpacity = 0.70,
            Text = Rgb(0xF4, 0xF6, 0xFA), SelectedText = Rgb(0x14, 0x17, 0x1D),
            Indicator = Rgb(0x7F, 0xB4, 0xFF),
            ShadowOpacity = 0.50, ShadowBlur = 38, ShadowDepth = 9,
            HoverBrightness = 0.07,
        },
    };

    // --- 7. Material 3 -------------------------------------------------------------------------------
    // Tonal surface with a tinted selection container rather than a white pill.
    private static ThemeDefinition Material3() => new()
    {
        Id = BarTheme.Material3,
        Name = "Material 3",
        Description = "Tonal Google-style surface with a tinted selection container.",
        Blur = BackdropBlur.None,
        CornerRadius = 16,
        ShowIndicator = true,
        OverLight = new ThemeVariant
        {
            Surface = Rgb(0xEE, 0xEB, 0xF4), SurfaceOpacity = 0.97,
            Border = Rgb(0x6A, 0x5A, 0x84), BorderOpacity = 0.14,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0xE5, 0xDE, 0xFF), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0x65, 0x50, 0xA6), SelectedBorderOpacity = 0.22,
            Text = Rgb(0x48, 0x45, 0x4E), SelectedText = Rgb(0x21, 0x00, 0x5D),
            Indicator = Rgb(0x65, 0x50, 0xA6),
            ShadowOpacity = 0.18, ShadowBlur = 14, ShadowDepth = 3,
            HoverBrightness = 0.05,
        },
        OverDark = new ThemeVariant
        {
            Surface = Rgb(0x2B, 0x28, 0x30), SurfaceOpacity = 0.97,
            Border = Rgb(0xCF, 0xBC, 0xFF), BorderOpacity = 0.18,
            SheenStrength = 0.0, BezelStrength = 0.0,
            SelectedSurface = Rgb(0x4F, 0x37, 0x8B), SelectedOpacity = 1.0,
            SelectedBorder = Rgb(0xCF, 0xBC, 0xFF), SelectedBorderOpacity = 0.34,
            Text = Rgb(0xCA, 0xC4, 0xD0), SelectedText = Rgb(0xEA, 0xDD, 0xFF),
            Indicator = Rgb(0xCF, 0xBC, 0xFF),
            ShadowOpacity = 0.40, ShadowBlur = 16, ShadowDepth = 4,
            HoverBrightness = 0.07,
        },
    };
}
