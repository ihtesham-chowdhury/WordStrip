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
        foreach (var (r, g, b, a) in AllSurfaces)
        {
            var adjusted = SurfaceSeparation.Ensure(new SurfaceTint(r, g, b, a), backdrop);

            Assert.True(SeparationOf(adjusted, backdrop) >= SurfaceSeparation.Floor - 0.001,
                $"#{r:X2}{g:X2}{b:X2} at {a:F2} over {backdrop:F2}: separation {SeparationOf(adjusted, backdrop):F3}");
        }
    }

    /// <summary>
    /// The floor is a safety net, not the designer. Every theme is authored to clear it over the backdrop it
    /// was drawn for, so the rescue path does not run in ordinary use and each theme looks as intended. Nine
    /// of the fourteen variants failed this when it was first written.
    ///
    /// <para>Two variants are listed as deliberate exceptions below, and they are the interesting ones: a
    /// theme whose whole identity is "barely there" and one whose identity is "as black as the editor behind
    /// it" can only exist because the floor will lift them when the backdrop makes them unreadable. The
    /// exception list is the contract - a third entry appearing here means a theme was authored carelessly,
    /// not that the rule changed.</para>
    /// </summary>
    [Fact]
    public void No_theme_needs_rescuing_over_the_backdrop_it_was_authored_for()
    {
        foreach (var (r, g, b, a, backdrop) in Authored)
        {
            // The thickness setting scales the authored opacity; 0.62 is the default.
            var tint = new SurfaceTint(r, g, b, Math.Min(1.0, a * (0.55 + (0.62 * 0.75))));

            Assert.Equal(tint, SurfaceSeparation.Ensure(tint, backdrop));
        }
    }

    [Fact]
    public void The_two_themes_that_rely_on_the_floor_are_rescued_by_it()
    {
        foreach (var (r, g, b, a, backdrop) in FloorDependent)
        {
            var tint = new SurfaceTint(r, g, b, Math.Min(1.0, a * (0.55 + (0.62 * 0.75))));
            var adjusted = SurfaceSeparation.Ensure(tint, backdrop);

            Assert.NotEqual(tint, adjusted);
            Assert.True(SeparationOf(adjusted, backdrop) >= SurfaceSeparation.Floor - 0.001);
        }
    }

    /// <summary>Every surface in the catalogue, light variant then dark, in catalogue order.</summary>
    private static readonly (byte R, byte G, byte B, double A)[] AllSurfaces =
    {
        (0xD1, 0xD9, 0xE7, 0.86), (0x39, 0x3F, 0x4A, 0.82),   // Fluent Surface
        (0xD3, 0xD8, 0xE3, 0.86), (0x41, 0x47, 0x54, 0.70),   // Spatial Glass
        (0xD6, 0xD9, 0xE0, 0.97), (0x2C, 0x31, 0x3A, 0.98),   // Command
        (0xDA, 0xD5, 0xE6, 0.98), (0x36, 0x32, 0x3F, 0.98),   // Material You
        (0xEA, 0xDC, 0xC2, 1.00), (0x43, 0x39, 0x2C, 1.00),   // Paper & Ink
        (0xDD, 0xDC, 0xD4, 1.00), (0x30, 0x30, 0x33, 1.00),   // Editorial
        (0x26, 0x2B, 0x2E, 0.98), (0x14, 0x17, 0x18, 1.00),   // Terminal
        (0xEE, 0xF0, 0xF4, 0.38), (0x24, 0x27, 0x2D, 0.42),   // Prism Rail
    };

    /// <summary>The variants that stand on their own: light over a white page, dark over a near-black editor.</summary>
    private static readonly (byte R, byte G, byte B, double A, double Backdrop)[] Authored =
    {
        (0xD1, 0xD9, 0xE7, 0.86, 1.0), (0x39, 0x3F, 0x4A, 0.82, 0.06),
        (0xD3, 0xD8, 0xE3, 0.86, 1.0), (0x41, 0x47, 0x54, 0.70, 0.06),
        (0xD6, 0xD9, 0xE0, 0.97, 1.0), (0x2C, 0x31, 0x3A, 0.98, 0.06),
        (0xDA, 0xD5, 0xE6, 0.98, 1.0), (0x36, 0x32, 0x3F, 0.98, 0.06),
        (0xEA, 0xDC, 0xC2, 1.00, 1.0), (0x43, 0x39, 0x2C, 1.00, 0.06),
        (0xDD, 0xDC, 0xD4, 1.00, 1.0), (0x30, 0x30, 0x33, 1.00, 0.06),
        (0x26, 0x2B, 0x2E, 0.98, 1.0),
    };

    /// <summary>
    /// The deliberate exceptions. Prism Rail is a wash by design - it is meant to be barely there, and the
    /// floor is what keeps its words readable over a photograph. Terminal's dark variant is OLED black over
    /// an OLED editor, which is the one case where "the same colour as the thing behind it" is the intent
    /// and the floor's lift is the only thing that keeps it from vanishing.
    /// </summary>
    private static readonly (byte R, byte G, byte B, double A, double Backdrop)[] FloorDependent =
    {
        (0x14, 0x17, 0x18, 1.00, 0.06),   // Terminal, dark
        (0xEE, 0xF0, 0xF4, 0.38, 1.0),    // Prism Rail, light
        (0x24, 0x27, 0x2D, 0.42, 0.06),   // Prism Rail, dark
    };
}
