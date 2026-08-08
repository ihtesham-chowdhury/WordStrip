using System.Runtime.InteropServices;

namespace WordStrip.App.UI;

/// <summary>
/// Samples how bright the screen is immediately outside the bar, so the glass can adapt to what it is
/// sitting on.
///
/// <para>Apple's regular material "adjusts the luminosity of background content to maintain legibility" —
/// glass is not one fixed colour. A permanently dark scrim works over a white document and then disappears
/// over a dark one, where it has nothing to contrast against and reads as a flat grey slab.</para>
///
/// <para>Sampling deliberately happens just <em>outside</em> the bar's own bounds. Reading the screen where
/// the bar already is would sample the bar itself and feed its own tint back in.</para>
/// </summary>
internal static class BackgroundProbe
{
    /// <summary>
    /// Mean perceived luminance, 0 (black) to 1 (white), of the strip of screen just above the bar — or just
    /// below it when the bar is at the top of the display. Returns null when nothing could be sampled.
    /// </summary>
    public static double? SampleAround(int left, int top, int width, int height)
    {
        if (width <= 0 || height <= 0) return null;

        const int probeOffset = 24;
        var y = top - probeOffset;
        if (y < 0) y = top + height + probeOffset;
        if (y < 0) return null;

        var screenDc = GetDC(0);
        if (screenDc == 0) return null;

        try
        {
            double total = 0;
            var samples = 0;

            // Nine points across the bar's width: enough to average out a stray dark word or bright
            // highlight without the cost of reading a whole region.
            for (var i = 1; i <= 9; i++)
            {
                var x = left + (int)(width * (i / 10.0));
                var colorRef = GetPixel(screenDc, x, y);
                if (colorRef == CLR_INVALID) continue;

                var r = colorRef & 0xFF;
                var g = (colorRef >> 8) & 0xFF;
                var b = (colorRef >> 16) & 0xFF;

                // Rec. 601 luma: cheap and close enough for a light/dark decision.
                total += (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
                samples++;
            }

            return samples == 0 ? null : total / samples;
        }
        finally
        {
            ReleaseDC(0, screenDc);
        }
    }

    private const uint CLR_INVALID = 0xFFFFFFFF;

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(nint hdc, int x, int y);
}
