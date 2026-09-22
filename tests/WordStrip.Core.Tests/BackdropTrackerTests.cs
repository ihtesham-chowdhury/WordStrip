using WordStrip.Core.Presentation;

namespace WordStrip.Core.Tests;

/// <summary>
/// The thing that stops the bar's material drifting while someone types. The behaviour that matters is the
/// negative one: for almost every measurement, nothing happens at all.
/// </summary>
public class BackdropTrackerTests
{
    [Fact]
    public void The_first_measurement_always_settles()
    {
        var tracker = new BackdropTracker();

        Assert.True(tracker.Update(0.93));
        Assert.NotNull(tracker.Settled);
    }

    [Fact]
    public void A_page_repainting_a_shade_lighter_changes_nothing()
    {
        var tracker = new BackdropTracker();
        tracker.Update(0.93);
        var settled = tracker.Settled;

        // The sort of wobble a line of text scrolling under the sample points produces.
        foreach (var sample in new[] { 0.95, 0.91, 0.97, 0.90, 0.94, 0.88 })
            Assert.False(tracker.Update(sample), $"re-tinted for {sample}");

        Assert.Equal(settled, tracker.Settled);
    }

    [Fact]
    public void Moving_from_a_white_page_to_a_dark_editor_does_change_it()
    {
        var tracker = new BackdropTracker();
        tracker.Update(0.95);

        Assert.True(tracker.Update(0.05));
        Assert.True(tracker.Settled < 0.3);
    }

    [Fact]
    public void A_measurement_sitting_on_a_boundary_does_not_flutter()
    {
        var tracker = new BackdropTracker();
        tracker.Update(0.30);

        // Right on the edge between two bands, wobbling either side of it.
        foreach (var sample in new[] { 0.40, 0.39, 0.41, 0.38, 0.42 })
            Assert.False(tracker.Update(sample), $"changed band for {sample}");
    }

    [Fact]
    public void The_settled_value_is_one_of_a_handful_of_positions()
    {
        var seen = new HashSet<double>();

        for (var measured = 0.0; measured <= 1.0; measured += 0.01)
        {
            var tracker = new BackdropTracker();
            tracker.Update(measured);
            seen.Add(tracker.Settled!.Value);
        }

        // Five bands across the range: the material has five possible tints, not a hundred.
        Assert.InRange(seen.Count, 2, 6);
    }

    [Fact]
    public void A_settled_value_always_represents_the_measurement_it_came_from()
    {
        for (var measured = 0.0; measured <= 1.0; measured += 0.02)
        {
            var tracker = new BackdropTracker();
            tracker.Update(measured);

            Assert.True(
                Math.Abs(tracker.Settled!.Value - measured) <= BackdropTracker.BandWidth / 2 + 0.001,
                $"{measured:F2} settled at {tracker.Settled:F2}");
        }
    }

    [Fact]
    public void Resetting_makes_the_next_measurement_count_again()
    {
        var tracker = new BackdropTracker();
        tracker.Update(0.9);
        tracker.Reset();

        Assert.Null(tracker.Settled);
        Assert.True(tracker.Update(0.9));
    }
}
