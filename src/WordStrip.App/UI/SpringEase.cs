using System.Windows;
using System.Windows.Media.Animation;

namespace WordStrip.App.UI;

/// <summary>
/// A damped-harmonic-oscillator easing curve, matching the shape of SwiftUI's
/// <c>spring(response:dampingFraction:)</c>. Apple's interfaces move on springs rather than fixed bezier
/// curves, which is why their motion reads as physical: velocity carries through, and the element settles
/// instead of stopping dead.
///
/// <para><b>Response</b> is the period of the underlying oscillation in seconds — smaller is snappier.
/// <b>DampingFraction</b> is how quickly it settles: 1.0 is critically damped (no overshoot), below 1.0
/// overshoots slightly and springs back, which is what gives Liquid Glass its fluid feel.</para>
///
/// <para>Set the animation's Duration to roughly the settle time; the curve is normalised so it reaches
/// 1.0 exactly at the end regardless, avoiding a visible jump when the animation is cut short.</para>
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    public static readonly DependencyProperty ResponseProperty =
        DependencyProperty.Register(nameof(Response), typeof(double), typeof(SpringEase), new PropertyMetadata(0.35));

    public static readonly DependencyProperty DampingFractionProperty =
        DependencyProperty.Register(nameof(DampingFraction), typeof(double), typeof(SpringEase), new PropertyMetadata(0.85));

    /// <summary>Oscillation period in seconds. Lower = faster, more urgent.</summary>
    public double Response
    {
        get => (double)GetValue(ResponseProperty);
        set => SetValue(ResponseProperty, value);
    }

    /// <summary>1.0 settles without overshoot; below 1.0 overshoots and springs back.</summary>
    public double DampingFraction
    {
        get => (double)GetValue(DampingFractionProperty);
        set => SetValue(DampingFractionProperty, value);
    }

    /// <summary>How long, in seconds, the curve represents. Should match the animation Duration.</summary>
    public double DurationSeconds { get; set; } = 0.45;

    protected override double EaseInCore(double normalizedTime)
    {
        var raw = SpringValue(normalizedTime * DurationSeconds);
        var atEnd = SpringValue(DurationSeconds);

        // Renormalise so the curve lands exactly on 1.0 at the end of the Duration. Without this a spring
        // that hasn't fully settled would leave the property short of its target and snap on completion.
        if (Math.Abs(atEnd) < 1e-6) return raw;
        return raw / atEnd;
    }

    private double SpringValue(double t)
    {
        if (t <= 0) return 0;

        var omega0 = 2 * Math.PI / Math.Max(Response, 1e-4);
        var zeta = Math.Max(DampingFraction, 0.0);

        if (zeta < 1.0)
        {
            var omegaD = omega0 * Math.Sqrt(1 - zeta * zeta);
            return 1 - Math.Exp(-zeta * omega0 * t) *
                (Math.Cos(omegaD * t) + zeta * omega0 / omegaD * Math.Sin(omegaD * t));
        }

        // Critically damped (and, near enough, overdamped): settles without crossing the target.
        return 1 - Math.Exp(-omega0 * t) * (1 + omega0 * t);
    }

    protected override Freezable CreateInstanceCore() => new SpringEase();
}
