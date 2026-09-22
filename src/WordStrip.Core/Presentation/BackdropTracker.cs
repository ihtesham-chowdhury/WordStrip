namespace WordStrip.Core.Presentation;

/// <summary>
/// Settles the measured backdrop brightness into one of a few values, so the bar's material stops drifting
/// while someone types.
///
/// <para><b>The problem this fixes.</b> The screen behind the bar is sampled every time typing pauses, and
/// the answer is never quite the same twice: a line of text scrolls under the sample points, a cursor
/// blinks, a page repaints a shade lighter. Feeding those raw numbers into the separation floor meant the
/// surface's opacity and tint were recomputed slightly differently each time, and over a long session that
/// reads exactly as the user described it - the bar quietly changing opacity and colour while they work.</para>
///
/// <para>So the measurement is quantised into wide bands and only leaves a band when it clears the boundary
/// by a margin. The floor then sees a handful of discrete values rather than a continuum, and the material
/// is either the same as it was or visibly, deliberately different.</para>
/// </summary>
public sealed class BackdropTracker
{
    /// <summary>
    /// How wide each band is. Wide on purpose: within a band the material does not change at all, and the
    /// separation floor has enough headroom that using the band's centre rather than the true measurement
    /// costs nothing visible.
    /// </summary>
    public const double BandWidth = 0.20;

    /// <summary>How far past a boundary a measurement must go before the band changes. Stops boundary flutter.</summary>
    public const double Margin = 0.05;

    /// <summary>The settled value, or null before anything has been measured.</summary>
    public double? Settled { get; private set; }

    /// <summary>
    /// Offers a fresh measurement. Returns true only when the settled value actually moved — which is the
    /// caller's cue to rebuild the palette, and is false for the overwhelming majority of samples.
    /// </summary>
    public bool Update(double measured)
    {
        var candidate = Quantise(measured);

        if (Settled is not { } settled)
        {
            Settled = candidate;
            return true;
        }

        // Inside the band, or not far enough past its edge: nothing changes.
        if (Math.Abs(measured - settled) <= (BandWidth / 2) + Margin) return false;
        if (Math.Abs(candidate - settled) < 0.001) return false;

        Settled = candidate;
        return true;
    }

    /// <summary>Forgets what was measured, so the next sample is taken at face value. For a focus change.</summary>
    public void Reset() => Settled = null;

    /// <summary>The centre of the band a measurement falls in.</summary>
    private static double Quantise(double measured)
    {
        var clamped = Math.Clamp(measured, 0, 1);
        var band = Math.Floor(clamped / BandWidth);

        return Math.Clamp((band * BandWidth) + (BandWidth / 2), 0, 1);
    }
}
