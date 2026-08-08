namespace WordStrip.App.UI;

/// <summary>
/// All animation timings in one place, scaled by a single user-facing speed multiplier.
///
/// <para>The defaults are deliberately brisk. A suggestion strip is read in the middle of typing, so the
/// motion's only job is to show <em>which</em> chip the selection moved to — any longer and it stops being
/// feedback and starts being something to wait for. Apple's own guidance warns against decorative motion
/// that outlives its purpose.</para>
///
/// <para><see cref="ForRepeat"/> covers the held-Tab case. When Tab auto-repeats at ~30 per second, a spring
/// tuned for a single deliberate press never gets near its target before being retargeted, so the lens
/// crawls and reads as stuck. Rapid cycling switches to a much shorter, critically damped spring that keeps
/// the lens glued to the selection.</para>
/// </summary>
public readonly record struct MotionProfile
{
    /// <summary>Cycling faster than this counts as a key-repeat scrub rather than individual presses.</summary>
    public static readonly TimeSpan RepeatThreshold = TimeSpan.FromMilliseconds(160);

    public required double LensSeconds { get; init; }
    public required double LensResponse { get; init; }
    public required double LensDamping { get; init; }

    public required double RevealSeconds { get; init; }
    public required double RevealResponse { get; init; }
    public required double RevealDamping { get; init; }

    public required double FadeInSeconds { get; init; }
    public required double DismissSeconds { get; init; }

    public static MotionProfile ForSpeed(double speed)
    {
        // Higher speed means shorter animations, so every duration divides by it.
        var s = 1.0 / Math.Clamp(speed, 0.5, 2.5);

        return new MotionProfile
        {
            LensSeconds = 0.20 * s,
            LensResponse = 0.15 * s,
            LensDamping = 0.85,

            RevealSeconds = 0.26 * s,
            RevealResponse = 0.20 * s,
            RevealDamping = 0.88,

            FadeInSeconds = 0.10 * s,
            DismissSeconds = 0.10 * s,
        };
    }

    /// <summary>
    /// A snappier variant for held-Tab scrubbing: critically damped so it tracks without overshoot, and
    /// short enough to land between repeats.
    /// </summary>
    public MotionProfile ForRepeat() => this with
    {
        LensSeconds = Math.Min(LensSeconds, 0.085),
        LensResponse = Math.Min(LensResponse, 0.07),
        LensDamping = 1.0,
    };
}
