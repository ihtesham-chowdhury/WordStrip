using System.Windows;
using System.Windows.Media;
using WordStrip.Core.Settings;
using Color = System.Windows.Media.Color;

namespace WordStrip.App.UI.Theming;

/// <summary>
/// How a theme says "this one". Selection is the strongest signal the bar has, so it is the first thing a
/// theme differs in — the others (material, density, typography) support it.
/// </summary>
public enum SelectionStyle
{
    /// <summary>A tonal surface behind the word with a small accent underline. Windows' own text-selection language.</summary>
    NativeTonal,

    /// <summary>A soft translucent capsule, as though that piece of the material had lifted slightly.</summary>
    SoftCapsule,

    /// <summary>A raised tonal block with a hairline edge: a key on an instrument, not a glowing pill.</summary>
    RaisedTonal,

    /// <summary>A filled tonal container. The most emphatic of the six.</summary>
    FilledTonal,

    /// <summary>An ink underline beneath the word and nothing else. Editorial rather than interactive.</summary>
    Underline,

    /// <summary>A solid block the width of the word, with the text knocked out of it. A terminal cursor.</summary>
    BlockCursor,

    /// <summary>
    /// A rail under the whole strip, lit from its left end to a travelling dot that sits under the selected
    /// word. Nothing is drawn behind the word itself, which is what lets this theme have no surface at all:
    /// the rail carries both the selection and the sense of where it is in the list.
    /// </summary>
    RailDot,
}

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

    /// <summary>How opaque the surface is drawn. Scaled by the material-thickness setting.</summary>
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

    /// <summary>The accent: an underline, a block cursor, or the mark beneath a tonal selection.</summary>
    public required Color Indicator { get; init; }

    /// <summary>Where the rail's gradient starts, for the one theme that has a rail. Ignored by the rest.</summary>
    public Color RailFrom { get; init; } = Color.FromRgb(0x3B, 0xC9, 0xF0);

    /// <summary>Where it ends. The dot sits at this colour, so it is the one the eye follows.</summary>
    public Color RailTo { get; init; } = Color.FromRgb(0xF0, 0x40, 0x88);

    public required double ShadowOpacity { get; init; }
    public required double ShadowBlur { get; init; }
    public required double ShadowDepth { get; init; }

    /// <summary>Brightness added to a chip on hover. Kept tiny — hover should register, not perform.</summary>
    public required double HoverBrightness { get; init; }
}

/// <summary>
/// A complete theme: its material in both environments, and the geometry, typography, density and motion
/// that go with it.
///
/// <para><b>A theme is not the same bar in another colour.</b> Each of the six differs from the others in at
/// least three of: material, geometry, candidate rhythm, typography, selection language, density and motion.
/// The test for that is to render them all in grey — if two are hard to tell apart, one of them is not
/// finished. <c>tests\regression\Verify-ThemeIdentity.ps1</c> checks the token side of it.</para>
/// </summary>
public sealed record ThemeDefinition
{
    public required BarTheme Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Two or three words for the gallery tile. Never a company's name.</summary>
    public required string Personality { get; init; }

    public required ThemeVariant OverLight { get; init; }
    public required ThemeVariant OverDark { get; init; }

    /// <summary>Backdrop blur this theme is designed around, used when the user leaves blur on Auto.</summary>
    public required BackdropBlur Blur { get; init; }

    /// <summary>
    /// Multiplies the density's outer radius. A terminal is nearly square (0.35), a luminous panel is
    /// generously rounded (1.35), and the silhouette is one of the things that tells them apart at a glance.
    /// </summary>
    public required double RadiusFactor { get; init; }

    /// <summary>
    /// How this theme leans when the bar is sizing itself: below 1 is tighter, above 1 is roomier. Applied
    /// only to automatic sizing — an explicit choice is an instruction, not a suggestion.
    /// </summary>
    public required double DensityBias { get; init; }

    /// <summary>How wide the gaps between candidates are, relative to the density's own gap.</summary>
    public required double RhythmFactor { get; init; }

    /// <summary>
    /// How much wider this theme's columns need to be than the type alone suggests. One for every
    /// proportional theme; more for the monospaced one, whose glyphs are all as wide as its widest, so the
    /// usual estimate from the font size leaves "forward to" shortened when it would have fitted.
    /// </summary>
    public double SlotWidthFactor { get; init; } = 1.0;

    public required SelectionStyle Selection { get; init; }

    /// <summary>How visible the rules between candidates are, 0 to 1. Zero leaves spacing to do the work.</summary>
    public required double DividerStrength { get; init; }

    /// <summary>
    /// The strip's own typeface, as a WPF fallback list. This never touches the application being typed
    /// into — it is the bar's voice, not the document's.
    /// </summary>
    public required string FontFamily { get; init; }

    /// <summary>The first candidate's weight. The alternates are always one step lighter than this.</summary>
    public required FontWeight PrimaryWeight { get; init; }

    /// <summary>Multiplies every animation duration: a terminal settles instantly, glass takes its time.</summary>
    public required double MotionFactor { get; init; }

    public ThemeVariant For(GlassAppearance appearance) =>
        appearance == GlassAppearance.OverDark ? OverDark : OverLight;

    /// <summary>Whether this theme draws a mark beneath the selected word, and so must reserve room for one.</summary>
    public bool ShowIndicator =>
        Selection is SelectionStyle.NativeTonal
            or SelectionStyle.Underline
            or SelectionStyle.RaisedTonal
            or SelectionStyle.RailDot;
}
