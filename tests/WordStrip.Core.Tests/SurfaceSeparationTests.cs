using WordStrip.Core.Presentation;

namespace WordStrip.Core.Tests;

/// <summary>
/// The floor that keeps the bar from melting into the page behind it, and — just as important — leaves
/// every theme alone when it is already distinguishable.
/// </summary>
public class SurfaceSeparationTests
{
    private static double Composited(SurfaceTint tint, double backdrop) =>
        (SurfaceSeparation.Luminance(tint.R, tint.G, tint.B) * tint.Alpha) + (backdrop * (1 - tint.Alpha));

    private static double SeparationOf(SurfaceTint tint, double backdrop) =>
        Math.Abs(Composited(tint, backdrop) - backdrop);

    [Fact]
    public void A_theme_that_already_separates_is_returned_untouched()
    {
        // Raycast over a white page: near-black at 96%. Nothing to fix.
        var tint = new SurfaceTint(0x1A, 0x1B, 0x1E, 0.96);

        Assert.Equal(tint, SurfaceSeparation.Ensure(tint, backdropLuminance: 1.0));
    }

    [Fact]
    public void With_no_measurement_the_theme_is_returned_untouched()
    {
        var tint = new SurfaceTint(0xFF, 0xFF, 0xFF, 0.62);

        Assert.Equal(tint, SurfaceSeparation.Ensure(tint, backdropLuminance: null));
    }

    [Fact]
    public void A_pale_translucent_theme_over_a_white_page_is_pushed_clear_of_it()
    {
        // visionOS over light: white at 62%, which composites to white on white.
        var tint = new SurfaceTint(0xFF, 0xFF, 0xFF, 0.62);

        var adjusted = SurfaceSeparation.Ensure(tint, backdropLuminance: 1.0);

        Assert.True(SeparationOf(adjusted, 1.0) >= SurfaceSeparation.Floor - 0.001,
            $"separation was {SeparationOf(adjusted, 1.0):F3}");
        Assert.True(SurfaceSeparation.Luminance(adjusted.R, adjusted.G, adjusted.B) < 1.0, "it should have gone deeper, not lighter");
    }

    [Fact]
    public void A_dark_theme_over_a_near_black_editor_is_lifted_off_it()
    {
        var tint = new SurfaceTint(0x16, 0x17, 0x1A, 0.97);

        var adjusted = SurfaceSeparation.Ensure(tint, backdropLuminance: 0.04);

        Assert.True(SeparationOf(adjusted, 0.04) >= SurfaceSeparation.Floor - 0.001,
            $"separation was {SeparationOf(adjusted, 0.04):F3}");
        Assert.True(SurfaceSeparation.Luminance(adjusted.R, adjusted.G, adjusted.B) > 0.04, "it should have been lifted, not deepened");
    }

    [Fact]
    public void Raising_the_opacity_is_preferred_to_changing_the_colour()
    {
        // Far enough apart in colour that a little more solidity clears the floor on its own.
        var tint = new SurfaceTint(0x80, 0x80, 0x80, 0.20);

        var adjusted = SurfaceSeparation.Ensure(tint, backdropLuminance: 1.0);

        Assert.Equal(0x80, adjusted.R);
        Assert.True(adjusted.Alpha > tint.Alpha);
        Assert.True(SeparationOf(adjusted, 1.0) >= SurfaceSeparation.Floor - 0.001);
    }

    [Fact]
    public void The_smallest_sufficient_opacity_is_used_rather_than_the_largest_allowed()
    {
        var tint = new SurfaceTint(0x80, 0x80, 0x80, 0.20);

        var adjusted = SurfaceSeparation.Ensure(tint, backdropLuminance: 1.0);

        Assert.True(adjusted.Alpha < tint.Alpha + 0.22, $"alpha went to {adjusted.Alpha:F3}");
    }

    [Fact]
    public void A_tinted_surface_keeps_its_hue_when_it_has_to_move()
    {
        // Material 3's lilac surface, made translucent enough that colour has to move.
        var tint = new SurfaceTint(0xEE, 0xEB, 0xFF, 0.55);

        var adjusted = SurfaceSeparation.Ensure(tint, backdropLuminance: 1.0);

        Assert.True(adjusted.B > adjusted.G, "the blue lean that makes it lilac must survive");
        Assert.True(adjusted.R > adjusted.G, "and so must the red one");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.04)]
    [InlineData(0.5)]
    [InlineData(0.96)]
    [InlineData(1.0)]
    public void Every_theme_clears_the_floor_over_every_backdrop(double backdrop)
    {
        // One surface per theme variant in the catalogue, light and dark, as authored.
        (byte R, byte G, byte B, double A)[] surfaces =
        {
            (0xD0, 0xD4, 0xDD, 0.84), (0x3B, 0x3E, 0x45, 0.80),   // Fluent Acrylic
            (0xD8, 0xD8, 0xDD, 0.95), (0x35, 0x38, 0x40, 0.95),   // Mica
            (0x3A, 0x3F, 0x49, 0.90), (0x38, 0x3D, 0x47, 0.88),   // Fluent Depth
            (0xD4, 0xD4, 0xDC, 0.80), (0x3F, 0x3F, 0x45, 0.76),   // Apple Frosted
            (0x20, 0x22, 0x26, 0.96), (0x33, 0x35, 0x3B, 0.97),   // Raycast
            (0xD0, 0xD5, 0xE0, 0.82), (0xE8, 0xEC, 0xF4, 0.48),   // visionOS
            (0xDA, 0xD5, 0xE6, 0.98), (0x36, 0x32, 0x3F, 0.98),   // Material 3
        };

        foreach (var (r, g, b, a) in surfaces)
        {
            var adjusted = SurfaceSeparation.Ensure(new SurfaceTint(r, g, b, a), backdrop);

            Assert.True(SeparationOf(adjusted, backdrop) >= SurfaceSeparation.Floor - 0.001,
                $"#{r:X2}{g:X2}{b:X2} at {a:F2} over {backdrop:F2}: separation {SeparationOf(adjusted, backdrop):F3}");
        }
    }

    /// <summary>
    /// The floor is a safety net, not the designer. Every theme is authored to clear it over the backdrop
    /// it was drawn for, so the rescue path never runs in ordinary use and each theme looks as intended.
    /// Nine of the fourteen variants failed this when it was first written.
    /// </summary>
    [Fact]
    public void No_theme_needs_rescuing_over_the_backdrop_it_was_authored_for()
    {
        // Light variants over a white page, dark variants over a near-black editor, at the default thickness.
        (byte R, byte G, byte B, double A, double Backdrop)[] authored =
        {
            (0xD0, 0xD4, 0xDD, 0.84, 1.0), (0x3B, 0x3E, 0x45, 0.80, 0.06),
            (0xD8, 0xD8, 0xDD, 0.95, 1.0), (0x35, 0x38, 0x40, 0.95, 0.06),
            (0x3A, 0x3F, 0x49, 0.90, 1.0), (0x38, 0x3D, 0x47, 0.88, 0.06),
            (0xD4, 0xD4, 0xDC, 0.80, 1.0), (0x3F, 0x3F, 0x45, 0.76, 0.06),
            (0x20, 0x22, 0x26, 0.96, 1.0), (0x33, 0x35, 0x3B, 0.97, 0.06),
            (0xD0, 0xD5, 0xE0, 0.82, 1.0), (0xE8, 0xEC, 0xF4, 0.48, 0.06),
            (0xDA, 0xD5, 0xE6, 0.98, 1.0), (0x36, 0x32, 0x3F, 0.98, 0.06),
        };

        foreach (var (r, g, b, a, backdrop) in authored)
        {
            // The thickness setting scales the authored opacity; 0.62 is the default.
            var tint = new SurfaceTint(r, g, b, Math.Min(1.0, a * (0.55 + (0.62 * 0.75))));

            Assert.Equal(tint, SurfaceSeparation.Ensure(tint, backdrop));
        }
    }
}
