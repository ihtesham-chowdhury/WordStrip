using WordStrip.Core.Presentation;
using WordStrip.Core.Settings;

namespace WordStrip.Core.Tests;

/// <summary>
/// Sizing the bar from the text it sits next to: the three authored densities, what automatic does between
/// them, and the hysteresis that stops it twitching.
/// </summary>
public class OpticalSizingTests
{
    /// <summary>Notepad at its default, measured at 100%: a caret about 15 units tall.</summary>
    private static readonly HostTextMetrics SmallText = new(15);

    /// <summary>A word processor or browser at a comfortable reading size.</summary>
    private static readonly HostTextMetrics NormalText = new(19);

    /// <summary>Accessibility scaling, or a heading.</summary>
    private static readonly HostTextMetrics LargeText = new(30);

    // --- The authored densities -------------------------------------------------------------------------

    [Fact]
    public void The_three_densities_land_in_their_intended_height_ranges()
    {
        Assert.InRange(OpticalSizing.Compact.BarHeight, 32, 38);
        Assert.InRange(OpticalSizing.Standard.BarHeight, 40, 46);
        Assert.InRange(OpticalSizing.Comfortable.BarHeight, 48, 58);
    }

    [Fact]
    public void An_explicit_choice_ignores_the_text_entirely()
    {
        Assert.Equal(OpticalSizing.Compact, OpticalSizing.For(BarSize.Compact, LargeText));
        Assert.Equal(OpticalSizing.Comfortable, OpticalSizing.For(BarSize.Comfortable, SmallText));
        Assert.Equal(OpticalSizing.Standard, OpticalSizing.For(BarSize.Standard, SmallText));
    }

    [Fact]
    public void A_theme_may_lean_on_automatic_sizing_but_never_on_an_explicit_choice()
    {
        var tight = OpticalSizing.For(BarSize.Automatic, NormalText, themeBias: 0.9);
        var roomy = OpticalSizing.For(BarSize.Automatic, NormalText, themeBias: 1.1);
        Assert.True(tight.BarHeight < roomy.BarHeight);

        Assert.Equal(
            OpticalSizing.For(BarSize.Standard, NormalText),
            OpticalSizing.For(BarSize.Standard, NormalText, themeBias: 0.9));
    }

    // --- Automatic ---------------------------------------------------------------------------------------

    [Fact]
    public void Small_text_gets_a_bar_near_the_compact_end()
    {
        var metrics = OpticalSizing.For(BarSize.Automatic, SmallText);

        Assert.InRange(metrics.BarHeight, OpticalSizing.Compact.BarHeight, 42);
        Assert.Equal(BarDensity.Compact, metrics.NearestDensity);
    }

    [Fact]
    public void Ordinary_text_gets_something_close_to_the_standard_bar()
    {
        var metrics = OpticalSizing.For(BarSize.Automatic, NormalText);

        Assert.InRange(metrics.BarHeight, OpticalSizing.Standard.BarHeight - 6, OpticalSizing.Standard.BarHeight + 6);
    }

    [Fact]
    public void Large_text_gets_a_bar_near_the_comfortable_end()
    {
        var metrics = OpticalSizing.For(BarSize.Automatic, LargeText);

        Assert.Equal(BarDensity.Comfortable, metrics.NearestDensity);
    }

    [Fact]
    public void An_unmeasurable_host_gets_the_standard_bar()
    {
        var metrics = OpticalSizing.For(BarSize.Automatic, HostTextMetrics.Unknown);

        Assert.Equal(BarDensity.Standard, metrics.NearestDensity);
    }

    [Fact]
    public void The_bar_never_leaves_its_bounds_however_extreme_the_text()
    {
        foreach (var caret in new double[] { 2, 6, 15, 19, 30, 60, 200 })
        {
            var metrics = OpticalSizing.For(BarSize.Automatic, new HostTextMetrics(caret));

            Assert.InRange(metrics.BarHeight, OpticalSizing.MinimumHeight, OpticalSizing.MaximumHeight);
        }
    }

    // --- Optical, not uniform ----------------------------------------------------------------------------

    [Fact]
    public void Compact_is_not_a_scaled_down_standard()
    {
        var small = OpticalSizing.Compact;
        var standard = OpticalSizing.Standard;

        var heightRatio = small.BarHeight / standard.BarHeight;
        var fontRatio = small.FontSize / standard.FontSize;
        var paddingRatio = small.PaddingY / standard.PaddingY;

        // Type holds up better than the bar shrinks; vertical padding gives up more than its share. A
        // uniform scale would make all three of these the same number, which is the squashed look.
        Assert.True(fontRatio > heightRatio + 0.03, $"font {fontRatio:F2} vs height {heightRatio:F2}");
        Assert.True(paddingRatio < heightRatio - 0.03, $"padding {paddingRatio:F2} vs height {heightRatio:F2}");
    }

    [Fact]
    public void Shadow_gives_way_faster_than_anything_else()
    {
        var small = OpticalSizing.Compact;
        var standard = OpticalSizing.Standard;

        Assert.True(
            small.ShadowScale / standard.ShadowScale < small.BarHeight / standard.BarHeight,
            "a small bar with a large shadow reads as a sticker");
    }

    [Fact]
    public void Every_interpolated_size_keeps_its_corners_and_its_gaps()
    {
        for (var caret = 6.0; caret <= 40; caret += 0.5)
        {
            var metrics = OpticalSizing.For(BarSize.Automatic, new HostTextMetrics(caret));

            Assert.True(metrics.OuterRadius >= OpticalSizing.Compact.OuterRadius - 0.01, "corners must not flatten");
            Assert.True(metrics.CandidateGap >= OpticalSizing.Compact.CandidateGap - 0.01, "candidates must not collide");
            Assert.True(metrics.FontSize >= OpticalSizing.Compact.FontSize - 0.01, "type must stay readable");
        }
    }

    [Fact]
    public void Sizes_move_in_step_as_the_text_grows()
    {
        var previous = OpticalSizing.For(BarSize.Automatic, new HostTextMetrics(6));

        for (var caret = 6.5; caret <= 40; caret += 0.5)
        {
            var metrics = OpticalSizing.For(BarSize.Automatic, new HostTextMetrics(caret));

            Assert.True(metrics.BarHeight >= previous.BarHeight, $"height went backwards at caret {caret}");
            Assert.True(metrics.FontSize >= previous.FontSize - 0.001, $"type went backwards at caret {caret}");
            previous = metrics;
        }
    }

    // --- Hysteresis --------------------------------------------------------------------------------------

    [Fact]
    public void A_twitching_caret_does_not_resize_the_bar()
    {
        var sizer = new OpticalSizer();
        sizer.Update(BarSize.Automatic, NormalText);
        var settled = sizer.Current;

        foreach (var jitter in new[] { 19.4, 18.7, 19.2, 18.9, 19.6 })
            Assert.False(sizer.Update(BarSize.Automatic, new HostTextMetrics(jitter)), $"resized for {jitter}");

        Assert.Equal(settled, sizer.Current);
    }

    [Fact]
    public void Moving_to_genuinely_different_text_does_resize_the_bar()
    {
        var sizer = new OpticalSizer();
        sizer.Update(BarSize.Automatic, SmallText);
        var small = sizer.Current;

        Assert.True(sizer.Update(BarSize.Automatic, LargeText));
        Assert.True(sizer.Current.BarHeight > small.BarHeight);
    }

    [Fact]
    public void Losing_sight_of_the_caret_keeps_the_size_it_had()
    {
        var sizer = new OpticalSizer();
        sizer.Update(BarSize.Automatic, SmallText);
        var small = sizer.Current;

        Assert.False(sizer.Update(BarSize.Automatic, HostTextMetrics.Unknown));
        Assert.Equal(small, sizer.Current);
    }

    [Fact]
    public void Choosing_a_fixed_size_applies_at_once_and_then_ignores_the_text()
    {
        var sizer = new OpticalSizer();
        sizer.Update(BarSize.Automatic, SmallText);

        Assert.True(sizer.Update(BarSize.Comfortable, SmallText));
        Assert.Equal(OpticalSizing.Comfortable, sizer.Current);

        Assert.False(sizer.Update(BarSize.Comfortable, LargeText));
        Assert.Equal(OpticalSizing.Comfortable, sizer.Current);
    }

    [Fact]
    public void A_change_of_theme_bias_is_applied_immediately()
    {
        var sizer = new OpticalSizer();
        sizer.Update(BarSize.Automatic, NormalText);
        var before = sizer.Current;

        Assert.True(sizer.Update(BarSize.Automatic, NormalText, themeBias: 0.88));
        Assert.True(sizer.Current.BarHeight < before.BarHeight);
    }
}
