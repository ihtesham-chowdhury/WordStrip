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

    /// <summary>
    /// True when the speed slider is at its maximum, which means "no animation at all" rather than "very
    /// fast animation".
    ///
    /// <para>The far end of the slider used to be 80 ms, which is quick but still visible — and for someone
    /// typing at speed, a highlight that slides is a highlight that is briefly in the wrong place. Turning
    /// motion off is a different thing from making it short, so the end of the travel is where it belongs:
    /// no extra control to find, and the direction the user is already dragging means what they expect.</para>
    /// </summary>
    public required bool IsInstant { get; init; }

    public required double LensSeconds { get; init; }
    public required double LensResponse { get; init; }
    public required double LensDamping { get; init; }

    public required double RevealSeconds { get; init; }
    public required double RevealResponse { get; init; }
    public required double RevealDamping { get; init; }

    public required double FadeInSeconds { get; init; }
    public required double DismissSeconds { get; init; }

    /// <summary>
    /// Scale the bar grows from as it appears. Below 1 so it expands into place rather than fading in at
    /// full size — the same gesture a macOS window makes when an application opens.
    /// </summary>
    public required double RevealScaleFrom { get; init; }

    /// <summary>
    /// Damping for the scale spring, deliberately well under 1 so it overshoots and settles back. This is
    /// the bounce: a critically damped scale arrives correctly and feels like nothing at all, whereas a
    /// slight overshoot is what reads as a physical object arriving.
    ///
    /// <para>Applied only to scale, never to position. Both bouncing together looks like a wobble rather
    /// than a pop, and the horizontal edges of a wide bar make any positional overshoot very visible.</para>
    /// </summary>
    public required double BounceDamping { get; init; }

    /// <summary>Scale the bar shrinks to as it leaves. Closing mirrors opening, which is what makes the pair read as one object appearing and going away.</summary>
    public required double DismissScaleTo { get; init; }

    public static MotionProfile ForSpeed(double speed)
    {
        var clamped = Math.Clamp(speed, Core.Settings.AppSettings.MinMotionSpeed, Core.Settings.AppSettings.MaxMotionSpeed);

        // Higher speed means shorter animations, so every duration divides by it.
        var s = 1.0 / clamped;

        return new MotionProfile
        {
            IsInstant = clamped >= Core.Settings.AppSettings.MaxMotionSpeed,

            LensSeconds = 0.20 * s,
            LensResponse = 0.15 * s,
            LensDamping = 0.85,

            RevealSeconds = 0.26 * s,
            RevealResponse = 0.20 * s,
            RevealDamping = 0.88,

            FadeInSeconds = 0.10 * s,
            DismissSeconds = 0.12 * s,

            RevealScaleFrom = 0.88,
            BounceDamping = 0.55,
            DismissScaleTo = 0.94,
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
