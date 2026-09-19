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
    private static AppSettings Migrate(AppSettings settings)
    {
        if (settings.PredictionCycleWindowMs == 900) settings.PredictionCycleWindowMs = 1200;
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
