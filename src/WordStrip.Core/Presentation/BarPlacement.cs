using WordStrip.Core.Automation;
using WordStrip.Core.Settings;

namespace WordStrip.Core.Presentation;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// Where the bar goes, as arithmetic on whole pixels.
///
/// <para>Separated from the window so it can be tested against the cases that are awkward to produce on a
/// real desktop: a caret at the bottom of the work area, a caret against the right-hand edge, a display
/// whose work area does not start at the origin (a second monitor, or a taskbar down one side), and a
/// window taller than the space it is being placed in.</para>
///
/// <para>Everything is in the target display's physical pixels. Whole pixels are the point: a bar placed on
/// a fractional coordinate renders with a soft rim and blurred text for as long as it sits there.</para>
/// </summary>
public static class BarPlacement
{
    /// <param name="work">The target display's work area — inside the taskbar, in physical pixels.</param>
    /// <param name="size">The bar's current physical size.</param>
    /// <param name="edgeGap">Distance from the work area's edge in the fixed placements.</param>
    /// <param name="caretGap">Distance from the caret's line when following the caret.</param>
    /// <param name="caret">Where the caret is, when it is known.</param>
    public static (int Left, int Top) Place(
        BarPosition position, PixelRect work, PixelRect size, int edgeGap, int caretGap, CaretRect? caret)
    {
        var centred = work.Left + ((work.Width - size.Width) / 2);

        var (left, top) = position switch
        {
            BarPosition.TopCenter => (centred, work.Top + edgeGap),
            BarPosition.NearCaret when caret is { } c => NearCaret(c, work, size, caretGap),
            _ => (centred, work.Bottom - size.Height - edgeGap),
        };

        return Clamp(left, top, work, size);
    }

    /// <summary>
    /// Anchored to the caret: centred on it, and on the far side of the line it sits on, so the bar never
    /// covers the words being written. Below by preference — that is where Windows' own suggestions appear —
    /// and above when there is no room below.
    /// </summary>
    private static (int Left, int Top) NearCaret(CaretRect caret, PixelRect work, PixelRect size, int gap)
    {
        var left = ((caret.Left + caret.Right) / 2) - (size.Width / 2);

        var below = caret.Bottom + gap;
        var above = caret.Top - size.Height - gap;

        var top = below + size.Height <= work.Bottom ? below
            : above >= work.Top ? above
            : below;

        return (left, top);
    }

    /// <summary>
    /// Keeps the bar inside the work area, so it can never sit under the taskbar or off the edge of the
    /// display. The lower bound wins when the bar is larger than the space — being clipped at the far edge
    /// is worse than being clipped at the near one, because the first slot is the one that matters.
    /// </summary>
    private static (int Left, int Top) Clamp(int left, int top, PixelRect work, PixelRect size)
    {
        const int margin = 4;

        var minLeft = work.Left + margin;
        var minTop = work.Top + margin;

        return (
            Math.Clamp(left, minLeft, Math.Max(minLeft, work.Right - size.Width - margin)),
            Math.Clamp(top, minTop, Math.Max(minTop, work.Bottom - size.Height - margin)));
    }
}
