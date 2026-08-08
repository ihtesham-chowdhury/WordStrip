namespace WordStrip.App.UI;

/// <summary>
/// Every size on the bar, derived from one scale factor and the theme's corner radius so the proportions
/// stay right at any thickness and in any theme.
///
/// <para>Concentricity is the reason this is centralised rather than hard-coded per element: the chip radius
/// has to be the plate radius minus the inset and rim, or the curves stop running parallel to each other.
/// Everything is in device-independent units, so it scales correctly on high-DPI displays.</para>
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

    public static GlassMetrics ForScale(double scale, double themeCornerRadius, bool showIndicator)
    {
        // Clamped so the strip can never collapse into an unreadable sliver or balloon into a panel.
        scale = Math.Clamp(scale, 0.7, 1.4);

        var inset = Math.Round(5 * scale);
        const double rim = 1.0;

        // 13px base: the strip is a typing aid read in passing, so it stays compact. Padding is generous
        // relative to the text rather than the other way round.
        var fontSize = Math.Round(13.5 * scale);
        var chipPaddingY = Math.Round(5 * scale);
        var chipHeight = Math.Round(fontSize * 1.42) + (chipPaddingY * 2);

        var plateRadius = Math.Round(themeCornerRadius * scale);
        var chipRadius = Math.Max(4, plateRadius - inset - rim);

        var indicatorThickness = showIndicator ? Math.Max(2, Math.Round(2.5 * scale)) : 0;
        var indicatorReserve = showIndicator ? Math.Round(6 * scale) : 0;

        return new GlassMetrics
        {
            Inset = inset,
            RimThickness = rim,
            PlateRadius = plateRadius,
            ChipRadius = chipRadius,
            ChipPaddingX = Math.Round(14 * scale),
            ChipPaddingY = chipPaddingY,
            ChipMinHeight = chipHeight,
            ChipMarginX = Math.Round(2 * scale),
            FontSize = fontSize,
            EdgeGap = Math.Round(14 * scale),
            IndicatorReserve = indicatorReserve,
            IndicatorThickness = indicatorThickness,
            IndicatorWidthFactor = 0.42,
        };
    }

    /// <summary>Approximate overall bar height, used to show the user what the thickness slider will produce.</summary>
    public double ApproximateBarHeight => ChipMinHeight + (Inset * 2) + (RimThickness * 2) + IndicatorReserve;
}
