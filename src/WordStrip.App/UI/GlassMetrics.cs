using WordStrip.App.UI.Theming;
using WordStrip.Core.Presentation;

namespace WordStrip.App.UI;

/// <summary>
/// Every size on the bar: the optical density decides how big, the theme decides what shape.
///
/// <para>Density comes from <see cref="OpticalSizing"/>, which sizes the bar from the text it is sitting
/// next to. This type is where those numbers meet a theme's own geometry — its corner radius factor, its
/// candidate rhythm, its selection language — and become the concrete values the renderers draw with.</para>
///
/// <para>Concentricity is why this is centralised rather than hard-coded per element: the chip radius has to
/// be the plate radius minus the inset and rim, or the curves stop running parallel. Everything is in
/// device-independent units, so it scales correctly on high-DPI displays.</para>
/// </summary>
public readonly record struct GlassMetrics
{
    public required double Inset { get; init; }
    public required double RimThickness { get; init; }
    public required double PlateRadius { get; init; }

    /// <summary>Corner radius of a chip and of the selection surface. Concentric with <see cref="PlateRadius"/>.</summary>
    public required double ChipRadius { get; init; }

    public required double ChipPaddingX { get; init; }
    public required double ChipPaddingY { get; init; }
    public required double ChipMinHeight { get; init; }
    public required double ChipMarginX { get; init; }
    public required double FontSize { get; init; }
    public required double EdgeGap { get; init; }

    /// <summary>Vertical room reserved beneath the chips for the position indicator. Zero when it's hidden.</summary>
    public required double IndicatorReserve { get; init; }

    public required double IndicatorThickness { get; init; }

    /// <summary>Indicator length as a fraction of the selected chip's width.</summary>
    public required double IndicatorWidthFactor { get; init; }

    /// <summary>The narrowest a candidate column may be before words start being shortened.</summary>
    public required double MinSlotWidth { get; init; }

    /// <summary>Multiplies the theme's authored shadow. A small bar carries a smaller shadow.</summary>
    public required double ShadowScale { get; init; }

    /// <summary>
    /// The sizes for one density and one theme.
    /// </summary>
    public static GlassMetrics For(DensityMetrics density, ThemeDefinition theme)
    {
        const double rim = 1.0;

        var inset = Math.Round(density.EdgeInset);
        var plateRadius = Math.Round(density.OuterRadius * theme.RadiusFactor);

        // The indicator is part of the selection language, not decoration: only the themes whose selection
        // is a mark beneath the word reserve room for one.
        var showsIndicator = theme.ShowIndicator;
        var indicatorThickness = showsIndicator
            ? Math.Max(2, Math.Round((theme.Selection == SelectionStyle.Underline ? 2.5 : 2.0) * density.IndicatorScale))
            : 0;
        var indicatorReserve = showsIndicator ? Math.Round(6 * density.IndicatorScale) : 0;

        // A block cursor is a rectangle by definition; a capsule is as round as it can be without the ends
        // meeting. Everything else stays concentric with the plate.
        var chipRadius = theme.Selection switch
        {
            SelectionStyle.BlockCursor => 1.0,
            SelectionStyle.SoftCapsule => Math.Round(density.ChipHeight / 2),
            _ => Math.Max(3, Math.Min(density.CandidateRadius, plateRadius - inset - rim)),
        };

        return new GlassMetrics
        {
            Inset = inset,
            RimThickness = rim,
            PlateRadius = plateRadius,
            ChipRadius = chipRadius,
            ChipPaddingX = Math.Round(density.PaddingX),
            ChipPaddingY = Math.Round(density.PaddingY),
            ChipMinHeight = density.ChipHeight,
            ChipMarginX = Math.Round(density.CandidateGap * theme.RhythmFactor / 2),
            FontSize = Math.Round(density.FontSize * 2) / 2,   // half-point steps: the type has a rhythm too
            EdgeGap = Math.Round(14 * (0.8 + (density.ShadowScale * 0.2))),
            IndicatorReserve = indicatorReserve,
            IndicatorThickness = indicatorThickness,
            IndicatorWidthFactor = theme.Selection == SelectionStyle.Underline ? 0.86 : 0.42,
            MinSlotWidth = Math.Round(density.CandidateMinWidth * theme.RhythmFactor),
            ShadowScale = density.ShadowScale,
        };
    }

    /// <summary>Overall bar height, used to show the user what a size choice will produce.</summary>
    public double ApproximateBarHeight => ChipMinHeight + (Inset * 2) + (RimThickness * 2) + IndicatorReserve;
}
