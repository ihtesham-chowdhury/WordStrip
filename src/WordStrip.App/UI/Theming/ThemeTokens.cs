using System.Windows.Media;
using WordStrip.Core.Settings;
using Color = System.Windows.Media.Color;

namespace WordStrip.App.UI.Theming;

/// <summary>
/// One theme's appearance in a single environment (light backdrop or dark backdrop).
///
/// <para>These are semantic tokens, not a colour dump: a theme says what its surface, selection, text and
/// accent <em>mean</em>, and the renderer turns that into brushes. Dark variants are authored, never derived
/// by inverting the light ones — inverted colours are how themes end up muddy.</para>
/// </summary>
public sealed record ThemeVariant
{
    /// <summary>Base surface colour before <see cref="SurfaceOpacity"/> is applied.</summary>
    public required Color Surface { get; init; }

    /// <summary>How opaque the surface is at the user's default thickness. Scaled by the thickness setting.</summary>
    public required double SurfaceOpacity { get; init; }

    public required Color Border { get; init; }
    public required double BorderOpacity { get; init; }

    /// <summary>Specular highlight along the lit edge. Zero disables it for flatter, more native themes.</summary>
    public required double SheenStrength { get; init; }

    /// <summary>Lensing band just inside the rim. Zero disables it.</summary>
    public required double BezelStrength { get; init; }

    public required Color SelectedSurface { get; init; }
    public required double SelectedOpacity { get; init; }
    public required Color SelectedBorder { get; init; }
    public required double SelectedBorderOpacity { get; init; }

    public required Color Text { get; init; }
    public required Color SelectedText { get; init; }

    /// <summary>The position indicator beneath the selected word.</summary>
    public required Color Indicator { get; init; }

    public required double ShadowOpacity { get; init; }
    public required double ShadowBlur { get; init; }
    public required double ShadowDepth { get; init; }

    /// <summary>Brightness added to a chip on hover. Kept tiny — hover should register, not perform.</summary>
    public required double HoverBrightness { get; init; }
}

/// <summary>A complete theme: how it looks over bright content, how it looks over dark, and its geometry.</summary>
public sealed record ThemeDefinition
{
    public required BarTheme Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    public required ThemeVariant OverLight { get; init; }
    public required ThemeVariant OverDark { get; init; }

    /// <summary>Backdrop blur this theme is designed around, used when the user leaves blur on Auto.</summary>
    public required BackdropBlur Blur { get; init; }

    /// <summary>Plate corner radius in device-independent units at the default bar thickness.</summary>
    public required double CornerRadius { get; init; }

    /// <summary>Whether the position indicator is drawn. Some themes carry selection on the surface alone.</summary>
    public required bool ShowIndicator { get; init; }

    public ThemeVariant For(GlassAppearance appearance) =>
        appearance == GlassAppearance.OverDark ? OverDark : OverLight;
}
