using WordStrip.Core.Automation;
using WordStrip.Core.Presentation;
using WordStrip.Core.Settings;

namespace WordStrip.Core.Tests;

/// <summary>
/// Where the bar lands. Every case is in one display's physical pixels, and several of them — a second
/// monitor whose work area does not start at the origin, a caret at the very bottom of the screen, a bar
/// wider than the space it has — are the ones that are painful to stage by hand on a real desktop.
/// </summary>
public class BarPlacementTests
{
    /// <summary>A 1920x1080 display with a 72px taskbar, the shape this was measured against.</summary>
    private static readonly PixelRect Work = new(0, 0, 1920, 1008);

    /// <summary>A second display to the right, at a different offset and with a taskbar down its left edge.</summary>
    private static readonly PixelRect SecondWork = new(2000, 100, 1600, 900);

    private static readonly PixelRect Bar = new(0, 0, 563, 72);

    private static (int Left, int Top) Place(BarPosition position, PixelRect work, CaretRect? caret = null) =>
        BarPlacement.Place(position, work, Bar, edgeGap: 21, caretGap: 12, caret);

    // --- The fixed placements ---------------------------------------------------------------------------

    [Fact]
    public void The_bottom_placement_sits_a_gap_above_the_taskbar_and_is_centred()
    {
        var (left, top) = Place(BarPosition.BottomCenter, Work);

        Assert.Equal((1920 - 563) / 2, left);
        Assert.Equal(1008 - 72 - 21, top);
    }

    [Fact]
    public void The_top_placement_sits_a_gap_below_the_top_of_the_work_area()
    {
        var (left, top) = Place(BarPosition.TopCenter, Work);

        Assert.Equal((1920 - 563) / 2, left);
        Assert.Equal(21, top);
    }

    [Fact]
    public void A_display_that_does_not_start_at_the_origin_is_measured_from_its_own_edges()
    {
        var (left, top) = Place(BarPosition.BottomCenter, SecondWork);

        Assert.Equal(2000 + ((1600 - 563) / 2), left);
        Assert.Equal(100 + 900 - 72 - 21, top);
    }

    // --- Following the caret ----------------------------------------------------------------------------

    [Fact]
    public void The_bar_is_centred_on_the_caret_and_below_its_line()
    {
        var caret = new CaretRect(800, 400, 802, 420);

        var (left, top) = Place(BarPosition.NearCaret, Work, caret);

        Assert.Equal(801 - (563 / 2), left);
        Assert.Equal(432, top);
    }

    [Fact]
    public void A_caret_near_the_bottom_puts_the_bar_above_the_line_rather_than_off_the_screen()
    {
        var caret = new CaretRect(800, 960, 802, 984);

        var (_, top) = Place(BarPosition.NearCaret, Work, caret);

        Assert.Equal(960 - 72 - 12, top);
        Assert.True(top + 72 < 960, "the bar must not cover the caret's own line");
    }

    [Fact]
    public void A_caret_at_the_right_edge_keeps_the_whole_bar_on_the_display()
    {
        var caret = new CaretRect(1910, 400, 1912, 420);

        var (left, _) = Place(BarPosition.NearCaret, Work, caret);

        Assert.True(left + 563 <= 1920 - 4, $"right edge at {left + 563}");
    }

    [Fact]
    public void A_caret_at_the_left_edge_keeps_the_whole_bar_on_the_display()
    {
        var caret = new CaretRect(2, 400, 4, 420);

        var (left, _) = Place(BarPosition.NearCaret, Work, caret);

        Assert.True(left >= 4, $"left edge at {left}");
    }

    [Fact]
    public void A_caret_on_the_second_display_places_the_bar_on_that_display()
    {
        var caret = new CaretRect(2800, 500, 2802, 520);

        var (left, top) = Place(BarPosition.NearCaret, SecondWork, caret);

        Assert.InRange(left, 2000, 2000 + 1600 - 563);
        Assert.InRange(top, 100, 100 + 900 - 72);
    }

    [Fact]
    public void Without_a_caret_the_caret_placement_falls_back_to_the_bottom()
    {
        Assert.Equal(Place(BarPosition.BottomCenter, Work), Place(BarPosition.NearCaret, Work));
    }

    // --- Degenerate cases -------------------------------------------------------------------------------

    [Fact]
    public void A_bar_wider_than_the_display_starts_at_the_near_edge_rather_than_off_the_left()
    {
        var narrow = new PixelRect(0, 0, 400, 1008);

        var (left, _) = BarPlacement.Place(
            BarPosition.BottomCenter, narrow, Bar, edgeGap: 21, caretGap: 12, caret: null);

        Assert.Equal(4, left);
    }

    [Fact]
    public void A_work_area_shorter_than_the_bar_still_places_it_inside_the_work_area()
    {
        var shallow = new PixelRect(0, 0, 1920, 60);

        var (_, top) = BarPlacement.Place(
            BarPosition.BottomCenter, shallow, Bar, edgeGap: 21, caretGap: 12, caret: null);

        Assert.Equal(4, top);
    }
}
