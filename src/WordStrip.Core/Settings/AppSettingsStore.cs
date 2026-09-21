using System.Text.Json;

namespace WordStrip.Core.Settings;

/// <summary>Loads/saves <see cref="AppSettings"/> as JSON under the user's local AppData folder.</summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? UserDataLocation.File("settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (settings is not null) return Migrate(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings file — fall through to defaults rather than crash startup.
        }

        return new AppSettings();
    }

    /// <summary>
    /// Brings forward values that were only ever an old default. The Tab cycle window was 900 ms and never
    /// shown in the settings window, so a saved 900 cannot be a choice anybody made — it is the old default,
    /// written back to disk with everything else, and it would otherwise pin every existing user to it.
    /// </summary>
    /// <summary>
    /// Brings a settings file written by an older version up to date.
    ///
    /// <para>The rule is that a preference the user actually expressed survives. A theme that no longer
    /// exists becomes the one it was merged into rather than silently reverting to the default, and a
    /// thickness slider that had been moved becomes the fixed density nearest to it rather than being
    /// replaced by automatic sizing the user never asked for.</para>
    /// </summary>
    internal static AppSettings Migrate(AppSettings settings)
    {
        if (settings.PredictionCycleWindowMs == 900) settings.PredictionCycleWindowMs = 1200;

        settings.Theme = settings.Theme switch
        {
            BarTheme.LegacyMica => BarTheme.FluentSurface,
            BarTheme.LegacyRaycast => BarTheme.Command,
            BarTheme.LegacyVision => BarTheme.SpatialGlass,
            var known when Enum.IsDefined(known) => known,

            // A number no version ever wrote, or one from a future build: the default is the honest answer.
            _ => BarTheme.FluentSurface,
        };

        // BarSize is absent from every file written before it existed, which deserialises as Automatic. A
        // thickness that was left alone means the user never expressed a size, so automatic is right. One
        // that was moved is a preference, and becomes the nearest fixed density.
        if (settings.BarSize == BarSize.Automatic)
        {
            settings.BarSize = settings.BarScale switch
            {
                <= 0.88 => BarSize.Compact,
                >= 1.12 => BarSize.Comfortable,
                _ => BarSize.Automatic,
            };
        }

        return settings;
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}
