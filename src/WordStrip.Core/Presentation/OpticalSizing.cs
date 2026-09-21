using WordStrip.Core.Settings;

namespace WordStrip.Core.Presentation;

/// <summary>
/// What the text being written looks like, in device-independent units.
///
/// <para>The caret is the one measurement every host gives us. A font size is not: a browser will not say
/// what it is rendering, and an editor's reported size is in whatever units it feels like. The caret's
/// height, on the other hand, is a rectangle the text service or Win32 hands over for the express purpose of
/// saying "text is this tall here".</para>
/// </summary>
/// <param name="CaretHeight">The caret's height in device-independent units, or 0 when unknown.</param>
/// <param name="DpiScale">The display's scale factor, for diagnostics; the caret height is already scaled.</param>
public readonly record struct HostTextMetrics(double CaretHeight, double DpiScale = 1.0)
{
    public static readonly HostTextMetrics Unknown = new(0);

    public bool IsKnown => CaretHeight > 1;

    /// <summary>
    /// The text's line height, estimated from the caret.
    ///
    /// <para>A caret spans a little more than the font's em box and a little less than the full line box.
    /// 1.15 is the factor that put Notepad at 11pt, Word at 11pt and a browser at 16px within a point of
    /// their real line heights — close enough for a size decision, and it degrades gently either way.</para>
    /// </summary>
    public double LineHeight => CaretHeight * 1.15;
}

/// <summary>The three densities the bar is authored at. Automatic sizing lands between them.</summary>
public enum BarDensity
{
    Compact,
    Standard,
    Comfortable,
}

/// <summary>
/// Every size the bar is drawn from, in device-independent units.
///
/// <para>One record rather than a scale factor, because optical sizing is not uniform scaling: between the
/// compact and comfortable ends the type grows by a quarter and the vertical padding doubles. A bar that
/// had simply been multiplied by 0.75 reads as a squashed standard bar, which is the complaint this whole
/// system exists to answer.</para>
/// </summary>
public readonly record struct DensityMetrics(
    double FontSize,
    double PaddingY,
    double PaddingX,
    double CandidateGap,
    double CandidateMinWidth,
    double CandidateRadius,
    double OuterRadius,
    double EdgeInset,
    double ShadowScale,
    double IndicatorScale)
{
    /// <summary>The height a chip occupies: the line box of the type, plus its own padding.</summary>
    public double ChipHeight => Math.Round((FontSize * 1.42) + (PaddingY * 2));

    /// <summary>The bar's overall height, which is what the eye compares against the host's text.</summary>
    public double BarHeight => Math.Round(ChipHeight + (EdgeInset * 2) + (6 * IndicatorScale));

    /// <summary>Which of the three authored densities this is closest to. For the Settings readout.</summary>
    public BarDensity NearestDensity =>
        BarHeight <= (OpticalSizing.Compact.BarHeight + OpticalSizing.Standard.BarHeight) / 2 ? BarDensity.Compact
        : BarHeight <= (OpticalSizing.Standard.BarHeight + OpticalSizing.Comfortable.BarHeight) / 2 ? BarDensity.Standard
        : BarDensity.Comfortable;
}

/// <summary>
/// Chooses the bar's size from the text it is sitting next to.
///
/// <para>Three densities are authored by hand — each is a design, not a multiple of the others — and
/// automatic sizing interpolates between them along a target height derived from the caret. Each property
/// travels at its own rate: type grows slowly, vertical padding quickly, the shadow quicker still, and the
/// radii and gaps keep floors so a compact bar stays a rounded strip rather than becoming a pill or a
/// rectangle.</para>
/// </summary>
public static class OpticalSizing
{
    /// <summary>Dense: notes, code, terminals, anything set small.</summary>
    public static readonly DensityMetrics Compact = new(
        FontSize: 12.0, PaddingY: 3.0, PaddingX: 10, CandidateGap: 6, CandidateMinWidth: 62,
        CandidateRadius: 5, OuterRadius: 9, EdgeInset: 3, ShadowScale: 0.55, IndicatorScale: 0.8);

    /// <summary>What most documents want, and what the bar has always been.</summary>
    public static readonly DensityMetrics Standard = new(
        FontSize: 13.5, PaddingY: 4.4, PaddingX: 14, CandidateGap: 8, CandidateMinWidth: 80,
        CandidateRadius: 7, OuterRadius: 13, EdgeInset: 5, ShadowScale: 1.0, IndicatorScale: 1.0);

    /// <summary>Generous: large text, accessibility scaling, presentations.</summary>
    public static readonly DensityMetrics Comfortable = new(
        FontSize: 15.5, PaddingY: 5.5, PaddingX: 18, CandidateGap: 11, CandidateMinWidth: 96,
        CandidateRadius: 9, OuterRadius: 17, EdgeInset: 7, ShadowScale: 1.3, IndicatorScale: 1.15);

    /// <summary>
    /// How much taller than the text's line height the bar should be. Tuned against real environments
    /// rather than derived: 2.05 puts Notepad's default text at the compact end, an 11-point document at
    /// standard, and accessibility-scaled text at the comfortable end, which is what each of those wants.
    /// </summary>
    private const double OpticalMultiplier = 2.05;

    /// <summary>Room for the bar's own edges, on top of the multiple of the line height.</summary>
    private const double SafetyPadding = 1.5;

    public static readonly double MinimumHeight = Compact.BarHeight;
    public static readonly double MaximumHeight = Comfortable.BarHeight + 6;

    /// <summary>The metrics for one of the fixed densities.</summary>
    public static DensityMetrics For(BarDensity density) => density switch
    {
        BarDensity.Compact => Compact,
        BarDensity.Comfortable => Comfortable,
        _ => Standard,
    };

    /// <summary>
    /// The size to draw at.
    /// </summary>
    /// <param name="size">The user's choice. Only <see cref="BarSize.Automatic"/> consults the host.</param>
    /// <param name="host">What the text around the caret measures.</param>
    /// <param name="themeBias">
    /// A theme's own density leaning, around 1.0: a terminal wants to sit tighter than a luminous glass
    /// panel even over identical text. Applied to the target height, never to the user's explicit choice,
    /// because a theme should not quietly override an instruction.
    /// </param>
    public static DensityMetrics For(BarSize size, HostTextMetrics host, double themeBias = 1.0)
    {
        if (size != BarSize.Automatic)
        {
            return For(size switch
            {
                BarSize.Compact => BarDensity.Compact,
                BarSize.Comfortable => BarDensity.Comfortable,
                _ => BarDensity.Standard,
            });
        }

        // Nothing measured — a host that reports no caret, or the bar pinned to the bottom of the screen
        // where there is no text to be next to. Standard is the safe answer, nudged by the theme.
        if (!host.IsKnown) return Interpolate(Normalise(Standard.BarHeight * themeBias));

        var target = Math.Clamp(
            ((host.LineHeight * OpticalMultiplier) + SafetyPadding) * themeBias,
            MinimumHeight,
            MaximumHeight);

        return Interpolate(Normalise(target));
    }

    /// <summary>Where a target height sits between the compact and comfortable ends, 0 to 1.</summary>
    private static double Normalise(double targetHeight) =>
        Math.Clamp(
            (targetHeight - Compact.BarHeight) / (Comfortable.BarHeight - Compact.BarHeight),
            0.0,
            1.0);

    /// <summary>
    /// Blends the authored densities. Each property has its own curve, which is the whole point: an
    /// exponent below 1 makes a property reach its larger value early (so it holds up at the small end),
    /// above 1 makes it stay small until the bar is genuinely roomy.
    /// </summary>
    private static DensityMetrics Interpolate(double t)
    {
        if (t <= 0) return Compact;
        if (t >= 1) return Comfortable;

        return new DensityMetrics(
            // Type is the content: it grows, but slowly, and stays readable at the small end.
            FontSize: Blend(Compact.FontSize, Comfortable.FontSize, Curve(t, 0.72)),

            // Vertical padding is what makes a bar feel roomy or tight, so it carries most of the change.
            PaddingY: Blend(Compact.PaddingY, Comfortable.PaddingY, Curve(t, 1.25)),
            PaddingX: Blend(Compact.PaddingX, Comfortable.PaddingX, t),

            CandidateGap: Blend(Compact.CandidateGap, Comfortable.CandidateGap, t),
            CandidateMinWidth: Blend(Compact.CandidateMinWidth, Comfortable.CandidateMinWidth, Curve(t, 0.85)),

            // Radii keep the small end from turning into a rectangle.
            CandidateRadius: Blend(Compact.CandidateRadius, Comfortable.CandidateRadius, Curve(t, 0.8)),
            OuterRadius: Blend(Compact.OuterRadius, Comfortable.OuterRadius, Curve(t, 0.8)),

            EdgeInset: Blend(Compact.EdgeInset, Comfortable.EdgeInset, Curve(t, 1.15)),

            // Elevation belongs to big surfaces. A small bar with a large shadow looks like a sticker.
            ShadowScale: Blend(Compact.ShadowScale, Comfortable.ShadowScale, Curve(t, 1.4)),
            IndicatorScale: Blend(Compact.IndicatorScale, Comfortable.IndicatorScale, t));
    }

    private static double Curve(double t, double exponent) => Math.Pow(Math.Clamp(t, 0, 1), exponent);

    private static double Blend(double from, double to, double t) => from + ((to - from) * t);
}
