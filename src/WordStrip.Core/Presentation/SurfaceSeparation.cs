namespace WordStrip.Core.Presentation;

/// <summary>A surface's colour and how opaque it is drawn, 0-255 per channel and 0-1 alpha.</summary>
public readonly record struct SurfaceTint(byte R, byte G, byte B, double Alpha);

/// <summary>
/// Keeps the bar distinguishable from whatever is behind it.
///
/// <para>Choosing a light or a dark variant is not enough on its own. A pale theme over a white document is
/// still a pale theme over a white document: the two luminances meet, the surface stops being a surface, and
/// the words look like they are floating on the page. The same happens at the other end, where a dark theme
/// over a near-black editor becomes a slab you can only find by its shadow.</para>
///
/// <para>So a floor is enforced on what the user actually sees: the composited luminance of the bar against
/// the measured luminance of the backdrop. The theme is asked to change as little as possible to clear it —
/// first by drawing a little more solidly, and only then by moving its own colour away from the backdrop,
/// keeping its hue. A theme that already clears the floor is returned untouched, which is nearly always.</para>
/// </summary>
public static class SurfaceSeparation
{
    /// <summary>
    /// The smallest difference in perceived luminance, 0-1, between the bar and its backdrop. Chosen against
    /// the two failure cases this exists for: at 0.12 a white page and a pale theme are still clearly two
    /// different surfaces, while a theme that was already comfortable is not touched at all.
    /// </summary>
    public const double Floor = 0.12;

    /// <summary>How much more solid a surface may be drawn before its colour is moved instead. Translucency is part of a theme's identity.</summary>
    private const double MaxAddedOpacity = 0.22;

    /// <summary>
    /// Aimed at slightly beyond the floor. A colour is 8 bits per channel, so the luminance actually
    /// achievable is quantised, and rounding to the nearest byte lands just under the floor as often as
    /// just over it.
    /// </summary>
    private const double Target = Floor + 0.005;

    /// <summary>Rec. 601 luma, 0-1. The same formula <c>BackgroundProbe</c> measures the backdrop with.</summary>
    public static double Luminance(byte r, byte g, byte b) => ((0.299 * r) + (0.587 * g) + (0.114 * b)) / 255.0;

    /// <param name="tint">What the theme asked for.</param>
    /// <param name="backdropLuminance">The measured backdrop, 0-1, or null when nothing was sampled.</param>
    public static SurfaceTint Ensure(SurfaceTint tint, double? backdropLuminance)
    {
        if (backdropLuminance is not { } backdrop) return tint;

        var surface = Luminance(tint.R, tint.G, tint.B);
        var difference = Math.Abs(surface - backdrop);

        // What the eye gets is the surface composited over the backdrop, so alpha scales the difference.
        if (difference * tint.Alpha >= Floor) return tint;

        // 1. Draw it more solidly, if that alone is enough. The cheapest possible change.
        var maxAlpha = Math.Min(1.0, tint.Alpha + MaxAddedOpacity);
        if (difference > 0.0001 && difference * maxAlpha >= Target)
            return tint with { Alpha = Math.Min(maxAlpha, Target / difference) };

        // 2. Otherwise move the surface away from the backdrop, in whichever direction it already leans —
        //    a pale theme over white gets slightly deeper rather than suddenly becoming a dark theme.
        var wanted = Target / maxAlpha;

        // Already the darker of the two, or identical to a light backdrop: go darker. Otherwise lighter.
        var darker = surface < backdrop || (Math.Abs(surface - backdrop) < 0.0001 && backdrop >= 0.5);
        var target = Math.Clamp(darker ? backdrop - wanted : backdrop + wanted, 0.0, 1.0);

        // Clamping can leave the target on the wrong side of the floor at the extremes (a black backdrop
        // cannot be darkened away from); fall back to the other direction rather than returning something
        // that still fails.
        if (Math.Abs(target - backdrop) < wanted - 0.0001)
            target = Math.Clamp(darker ? backdrop + wanted : backdrop - wanted, 0.0, 1.0);

        return Shift(tint, surface, target) with { Alpha = maxAlpha };
    }

    /// <summary>
    /// Moves a colour to a target luminance by blending it towards black or white, which keeps its hue: the
    /// Material tint stays lilac and the Raycast surface stays neutral, they just sit further from the page.
    /// </summary>
    private static SurfaceTint Shift(SurfaceTint tint, double from, double to)
    {
        if (to > from)
        {
            var t = from >= 0.9999 ? 0 : (to - from) / (1 - from);
            return tint with
            {
                R = Blend(tint.R, 255, t),
                G = Blend(tint.G, 255, t),
                B = Blend(tint.B, 255, t),
            };
        }

        var towardsBlack = from <= 0.0001 ? 0 : 1 - (to / from);
        return tint with
        {
            R = Blend(tint.R, 0, towardsBlack),
            G = Blend(tint.G, 0, towardsBlack),
            B = Blend(tint.B, 0, towardsBlack),
        };
    }

    private static byte Blend(byte from, byte to, double t) =>
        (byte)Math.Clamp(Math.Round(from + ((to - from) * Math.Clamp(t, 0, 1))), 0, 255);
}
