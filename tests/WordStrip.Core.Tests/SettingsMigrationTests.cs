using WordStrip.Core.Settings;

namespace WordStrip.Core.Tests;

/// <summary>
/// What happens to a settings file written by an older version. The rule being protected is that a
/// preference the user actually expressed survives an upgrade — silently resetting someone's theme is worse
/// than never having merged the themes at all.
/// </summary>
public class SettingsMigrationTests
{
    [Theory]
    [InlineData(BarTheme.LegacyMica, BarTheme.FluentSurface)]
    [InlineData(BarTheme.LegacyRaycast, BarTheme.Command)]
    [InlineData(BarTheme.LegacyVision, BarTheme.SpatialGlass)]
    public void A_merged_theme_becomes_the_one_it_was_merged_into(BarTheme stored, BarTheme expected)
    {
        var settings = AppSettingsStore.Migrate(new AppSettings { Theme = stored });

        Assert.Equal(expected, settings.Theme);
    }

    [Theory]
    [InlineData(BarTheme.FluentSurface)]
    [InlineData(BarTheme.Command)]
    [InlineData(BarTheme.SpatialGlass)]
    [InlineData(BarTheme.MaterialYou)]
    [InlineData(BarTheme.PaperInk)]
    [InlineData(BarTheme.TerminalMono)]
    public void A_theme_that_still_exists_is_left_alone(BarTheme theme)
    {
        Assert.Equal(theme, AppSettingsStore.Migrate(new AppSettings { Theme = theme }).Theme);
    }

    [Fact]
    public void A_number_no_version_ever_wrote_falls_back_to_the_default()
    {
        var settings = AppSettingsStore.Migrate(new AppSettings { Theme = (BarTheme)94 });

        Assert.Equal(BarTheme.FluentSurface, settings.Theme);
    }

    [Fact]
    public void A_thickness_that_was_never_touched_becomes_automatic_sizing()
    {
        var settings = AppSettingsStore.Migrate(new AppSettings { BarScale = 1.0 });

        Assert.Equal(BarSize.Automatic, settings.BarSize);
    }

    [Theory]
    [InlineData(0.7, BarSize.Compact)]
    [InlineData(0.85, BarSize.Compact)]
    [InlineData(1.2, BarSize.Comfortable)]
    [InlineData(1.4, BarSize.Comfortable)]
    public void A_thickness_that_was_moved_becomes_the_nearest_fixed_density(double scale, BarSize expected)
    {
        var settings = AppSettingsStore.Migrate(new AppSettings { BarScale = scale });

        Assert.Equal(expected, settings.BarSize);
    }

    [Fact]
    public void A_size_already_chosen_in_this_version_is_never_overwritten()
    {
        var settings = AppSettingsStore.Migrate(new AppSettings { BarSize = BarSize.Standard, BarScale = 0.7 });

        Assert.Equal(BarSize.Standard, settings.BarSize);
    }

    [Fact]
    public void The_cycle_window_migration_still_applies()
    {
        Assert.Equal(1200, AppSettingsStore.Migrate(new AppSettings { PredictionCycleWindowMs = 900 }).PredictionCycleWindowMs);
    }
}
