using WordStrip.Core.Settings;

namespace WordStrip.Core.Presentation;

/// <summary>
/// Holds the size the bar is currently drawn at, and decides when it is worth changing.
///
/// <para><b>Hysteresis is the whole job.</b> The caret's reported height twitches — a taller glyph, a line
/// with a different font run, a host that rounds differently at each end of a scroll — and a bar that
/// re-sized on every twitch would be far worse than one that is slightly too big. So a new measurement has
/// to clear a band before it is accepted, and inside the band the current size simply stands.</para>
///
/// <para>The result the user should be able to describe is "it picked a size for this", never "it keeps
/// changing size".</para>
/// </summary>
public sealed class OpticalSizer
{
    /// <summary>How much the bar's height must want to move before it is allowed to, in device-independent units.</summary>
    private const double HeightTolerance = 3.0;

    /// <summary>How much the measured text must change before the question is even asked.</summary>
    private const double CaretTolerance = 0.12;

    private HostTextMetrics _accepted = HostTextMetrics.Unknown;
    private BarSize _size = BarSize.Automatic;
    private double _bias = 1.0;

    public OpticalSizer()
    {
        Current = OpticalSizing.For(BarSize.Automatic, HostTextMetrics.Unknown);
    }

    /// <summary>The metrics the bar is drawn from right now.</summary>
    public DensityMetrics Current { get; private set; }

    /// <summary>The measurement the current size was chosen for. Diagnostics only.</summary>
    public HostTextMetrics AcceptedMetrics => _accepted;

    /// <summary>
    /// Offers a new measurement. Returns true when the bar's size actually changed, which is the caller's
    /// cue to re-apply appearance — and, because it is false almost every time, is also what keeps this off
    /// the typing path.
    /// </summary>
    public bool Update(BarSize size, HostTextMetrics host, double themeBias = 1.0)
    {
        // An explicit choice, or a different theme, is an instruction rather than a measurement: apply it at
        // once and let the next measurement be judged against the result.
        if (size != _size || Math.Abs(themeBias - _bias) > 0.001)
        {
            _size = size;
            _bias = themeBias;
            _accepted = host;
            return Replace(OpticalSizing.For(size, host, themeBias));
        }

        if (size != BarSize.Automatic) return false;

        // Nothing to measure: keep whatever was last decided rather than snapping back to standard, since
        // the caret disappears routinely — between windows, during a selection, in a host that stops
        // reporting it — and none of those mean the text got bigger.
        if (!host.IsKnown) return false;

        if (_accepted.IsKnown)
        {
            var change = Math.Abs(host.CaretHeight - _accepted.CaretHeight) / _accepted.CaretHeight;
            if (change < CaretTolerance) return false;
        }

        var candidate = OpticalSizing.For(size, host, themeBias);
        if (Math.Abs(candidate.BarHeight - Current.BarHeight) < HeightTolerance) return false;

        _accepted = host;
        return Replace(candidate);
    }

    /// <summary>Drops what was measured, so the next measurement is taken at face value. For a focus change.</summary>
    public void Forget() => _accepted = HostTextMetrics.Unknown;

    private bool Replace(DensityMetrics metrics)
    {
        if (metrics == Current) return false;

        Current = metrics;
        return true;
    }
}
