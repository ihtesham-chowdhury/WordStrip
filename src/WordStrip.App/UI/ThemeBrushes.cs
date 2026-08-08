using System.Windows.Media;
using WordStrip.App.UI.Theming;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace WordStrip.App.UI;

/// <summary>Which way the material is tuned, chosen from the luminance of whatever is behind the bar.</summary>
public enum GlassAppearance
{
    /// <summary>Backdrop is bright — the theme's over-light variant.</summary>
    OverLight,

    /// <summary>Backdrop is dark — the theme's over-dark variant.</summary>
    OverDark,
}

/// <summary>
/// Turns a theme's semantic tokens into the frozen brushes the renderer draws with.
///
/// <para>This is the only place tokens become pixels, which is what keeps seven themes from becoming seven
/// implementations. It also owns the two accessibility fallbacks: when the user has reduced transparency the
/// surfaces go fully opaque while keeping the theme's colours, and under a High Contrast theme everything
/// collapses to system-level contrast because at that point the theme's identity matters less than being
/// readable at all.</para>
/// </summary>
public sealed class ThemeBrushes
{
    public required Brush Scrim { get; init; }
    public required Brush Sheen { get; init; }
    public required Brush Hairline { get; init; }
    public required Brush Bezel { get; init; }
    public required Brush Pill { get; init; }
    public required Brush PillRim { get; init; }
    public required Brush Indicator { get; init; }
    public required Color TextColor { get; init; }
    public required Color SelectedTextColor { get; init; }
    public required Color HoverOverlay { get; init; }
    public required double ShadowOpacity { get; init; }
    public required double ShadowBlur { get; init; }
    public required double ShadowDepth { get; init; }
    public required bool ShowIndicator { get; init; }

    public static ThemeBrushes Build(
        ThemeDefinition theme,
        GlassAppearance appearance,
        double thickness,
        bool allowTransparency,
        bool highContrast)
    {
        if (highContrast) return HighContrast();

        var v = theme.For(appearance);

        // The thickness slider scales the theme's authored opacity rather than replacing it, so a theme
        // designed to be nearly solid stays nearly solid and an airy one stays airy.
        var surfaceOpacity = allowTransparency
            ? Math.Clamp(v.SurfaceOpacity * (0.55 + thickness * 0.75), 0.10, 1.0)
            : 1.0;

        return new ThemeBrushes
        {
            Scrim = Solid(v.Surface, surfaceOpacity),
            Sheen = v.SheenStrength <= 0 || !allowTransparency
                ? Brushes.Transparent
                : VerticalGradient(
                    (0.00, 0xFF, 0xFF, 0xFF, 0.42 * v.SheenStrength),
                    (0.30, 0xFF, 0xFF, 0xFF, 0.14 * v.SheenStrength),
                    (0.68, 0xFF, 0xFF, 0xFF, 0.00),
                    (1.00, 0xFF, 0xFF, 0xFF, 0.16 * v.SheenStrength)),
            Hairline = VerticalGradient(
                (0.00, v.Border.R, v.Border.G, v.Border.B, v.BorderOpacity),
                (0.55, v.Border.R, v.Border.G, v.Border.B, v.BorderOpacity * 0.45),
                (1.00, v.Border.R, v.Border.G, v.Border.B, v.BorderOpacity * 0.62)),
            Bezel = v.BezelStrength <= 0 || !allowTransparency
                ? Brushes.Transparent
                : VerticalGradient(
                    (0.00, 0xFF, 0xFF, 0xFF, 0.34 * v.BezelStrength),
                    (0.32, 0xFF, 0xFF, 0xFF, 0.06 * v.BezelStrength),
                    (0.72, 0x00, 0x00, 0x00, 0.09 * v.BezelStrength),
                    (1.00, 0x00, 0x00, 0x00, 0.26 * v.BezelStrength)),
            Pill = Solid(v.SelectedSurface, v.SelectedOpacity),
            PillRim = Solid(v.SelectedBorder, v.SelectedBorderOpacity),
            Indicator = Solid(v.Indicator, 1.0),
            TextColor = v.Text,
            SelectedTextColor = v.SelectedText,
            HoverOverlay = Color.FromArgb((byte)Math.Round(v.HoverBrightness * 255), 0xFF, 0xFF, 0xFF),
            ShadowOpacity = allowTransparency ? v.ShadowOpacity : v.ShadowOpacity * 0.6,
            ShadowBlur = v.ShadowBlur,
            ShadowDepth = v.ShadowDepth,
            ShowIndicator = theme.ShowIndicator,
        };
    }

    /// <summary>
    /// Flat, system-driven fallback. Under High Contrast the user has asked the OS for guaranteed contrast,
    /// which outranks any theme's look.
    /// </summary>
    private static ThemeBrushes HighContrast() => new()
    {
        Scrim = Solid(Color.FromRgb(0x00, 0x00, 0x00), 1.0),
        Sheen = Brushes.Transparent,
        Hairline = Solid(Color.FromRgb(0xFF, 0xFF, 0xFF), 1.0),
        Bezel = Brushes.Transparent,
        Pill = Solid(Color.FromRgb(0xFF, 0xFF, 0xFF), 1.0),
        PillRim = Solid(Color.FromRgb(0xFF, 0xFF, 0xFF), 1.0),
        Indicator = Solid(Color.FromRgb(0xFF, 0xFF, 0x00), 1.0),
        TextColor = Color.FromRgb(0xFF, 0xFF, 0xFF),
        SelectedTextColor = Color.FromRgb(0x00, 0x00, 0x00),
        HoverOverlay = Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF),
        ShadowOpacity = 0,
        ShadowBlur = 0,
        ShadowDepth = 0,
        ShowIndicator = true,
    };

    private static Brush Solid(Color color, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Brush VerticalGradient(params (double Offset, byte R, byte G, byte B, double A)[] stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        foreach (var (offset, r, g, b, a) in stops)
        {
            brush.GradientStops.Add(new GradientStop(
                Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), r, g, b), offset));
        }

        brush.Freeze();
        return brush;
    }
}
