using System.ComponentModel;
using System.Runtime.CompilerServices;
using WordStrip.App.UI.Theming;
using WordStrip.Core.Personal;
using WordStrip.Core.Platform;
using WordStrip.Core.Settings;

namespace WordStrip.App.UI;

/// <summary>One row in the theme picker.</summary>
public sealed record ThemeChoice(BarTheme Id, string Name, string Description);

/// <summary>
/// Binding wrapper around the shared <see cref="AppSettings"/> instance. Every setter persists immediately
/// and pushes the change straight into the live bar, so the settings window doubles as a preview: there is
/// no Apply button because there is nothing to apply.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _store;
    private readonly string _executablePath;
    private readonly Action _onAppearanceChanged;

    private readonly PersonalVocabularyStore? _personalVocabulary;
    private readonly PersonalLanguageModel? _personalLearning;

    public SettingsViewModel(
        AppSettings settings,
        AppSettingsStore store,
        string executablePath,
        Action onAppearanceChanged,
        PersonalVocabularyStore? personalVocabulary = null,
        PersonalLanguageModel? personalLearning = null)
    {
        _settings = settings;
        _store = store;
        _executablePath = executablePath;
        _onAppearanceChanged = onAppearanceChanged;
        _personalVocabulary = personalVocabulary;
        _personalLearning = personalLearning;

        if (_personalVocabulary is not null)
            _personalVocabulary.Changed += (_, _) => RefreshPersonalWords();

        RefreshPersonalWords();
    }

    // --- Personal vocabulary ------------------------------------------------------------------------

    /// <summary>The user's own words, newest changes reflected immediately. Bound to the list in the settings window.</summary>
    public System.Collections.ObjectModel.ObservableCollection<string> PersonalWords { get; } = new();

    public bool HasPersonalVocabulary => _personalVocabulary is not null;

    public string PersonalWordCountLabel => _personalVocabulary is null
        ? string.Empty
        : _personalVocabulary.Count switch
        {
            0 => "No words added yet",
            1 => "1 word",
            var n => $"{n} words",
        };

    private string _newPersonalWord = string.Empty;

    /// <summary>Text in the "add a word" box.</summary>
    public string NewPersonalWord
    {
        get => _newPersonalWord;
        set
        {
            if (_newPersonalWord == value) return;
            _newPersonalWord = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAddPersonalWord));
        }
    }

    public bool CanAddPersonalWord =>
        _personalVocabulary is not null && PersonalVocabularyStore.Normalize(_newPersonalWord).Length > 0;

    public bool AddPersonalWord()
    {
        if (_personalVocabulary is null) return false;

        // The typed form is stored as-is so casing survives: someone adding "QNAP" means QNAP, not "qnap".
        if (!_personalVocabulary.Add(_newPersonalWord)) return false;

        _personalVocabulary.Save();
        NewPersonalWord = string.Empty;
        return true;
    }

    public void RemovePersonalWord(string? display)
    {
        if (_personalVocabulary is null || string.IsNullOrWhiteSpace(display)) return;
        if (!_personalVocabulary.Remove(display)) return;

        _personalVocabulary.Save();
    }

    public int ImportPersonalWords(string path) => _personalVocabulary?.ImportFrom(path) ?? 0;

    public void ExportPersonalWords(string path) => _personalVocabulary?.ExportTo(path);

    public string PersonalVocabularyPath => _personalVocabulary?.FilePath ?? string.Empty;

    private void RefreshPersonalWords()
    {
        if (_personalVocabulary is null) return;

        PersonalWords.Clear();
        foreach (var word in _personalVocabulary.GetAll())
            PersonalWords.Add(word.Display);

        OnPropertyChanged(nameof(PersonalWordCountLabel));
    }

    // --- Personal learning --------------------------------------------------------------------------

    public bool HasPersonalLearning => _personalLearning is not null;

    public bool PersonalLearningEnabled
    {
        get => _settings.PersonalLearningEnabled;
        set
        {
            if (_settings.PersonalLearningEnabled == value) return;
            _settings.PersonalLearningEnabled = value;
            Persist();
            OnPropertyChanged();
            OnPropertyChanged(nameof(LearnedDataLabel));
        }
    }

    /// <summary>
    /// Plain-language account of what has been learned. Shown because a feature that quietly records how
    /// someone types should be able to say exactly how much it has recorded.
    /// </summary>
    public string LearnedDataLabel
    {
        get
        {
            if (_personalLearning is null) return string.Empty;
            if (_personalLearning.WordsLearned == 0) return "Nothing learned yet";

            return $"{_personalLearning.WordsLearned:N0} words seen · " +
                   $"{_personalLearning.UnigramCount:N0} words, {_personalLearning.BigramCount:N0} pairs, " +
                   $"{_personalLearning.TrigramCount:N0} triples remembered";
        }
    }

    public void ClearLearnedData()
    {
        _personalLearning?.Clear();
        OnPropertyChanged(nameof(LearnedDataLabel));
    }

    public void RefreshLearnedDataLabel() => OnPropertyChanged(nameof(LearnedDataLabel));

    public IReadOnlyList<ThemeChoice> Themes { get; } =
        ThemeCatalog.All.Select(t => new ThemeChoice(t.Id, t.Name, t.Description)).ToList();

    public ThemeChoice? SelectedTheme
    {
        get => Themes.FirstOrDefault(t => t.Id == _settings.Theme);
        set
        {
            if (value is null || _settings.Theme == value.Id) return;
            _settings.Theme = value.Id;
            Persist();
            NotifyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedThemeDescription));
        }
    }

    public string SelectedThemeDescription => SelectedTheme?.Description ?? string.Empty;

    public int MinSuggestionCount => AppSettings.MinSuggestionCount;
    public int MaxSuggestionCount => AppSettings.MaxSuggestionCount;
    public double MinGlassTint => AppSettings.MinGlassTint;
    public double MaxGlassTint => AppSettings.MaxGlassTint;

    public int SuggestionCount
    {
        get => _settings.SuggestionCount;
        set
        {
            if (_settings.SuggestionCount == value) return;
            _settings.SuggestionCount = value;
            Persist();
            NotifyAppearance();
            OnPropertyChanged();
        }
    }

    public double GlassTint
    {
        get => _settings.GlassTint;
        set
        {
            if (Math.Abs(_settings.GlassTint - value) < 0.001) return;
            _settings.GlassTint = value;
            Persist();
            NotifyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(GlassTintPercent));
        }
    }

    public string GlassTintPercent => $"{_settings.GlassTint * 100:0}%";

    public double MinBarScale => AppSettings.MinBarScale;
    public double MaxBarScale => AppSettings.MaxBarScale;

    public double BarScale
    {
        get => _settings.BarScale;
        set
        {
            if (Math.Abs(_settings.BarScale - value) < 0.001) return;
            _settings.BarScale = value;
            Persist();
            NotifyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(BarScaleLabel));
        }
    }

    /// <summary>Describes the thickness in the terms the user cares about rather than as a raw multiplier.</summary>
    public string BarScaleLabel
    {
        get
        {
            var theme = ThemeCatalog.Get(_settings.Theme);
            var height = GlassMetrics.ForScale(_settings.BarScale, theme.CornerRadius, theme.ShowIndicator).ApproximateBarHeight;
            var name = _settings.BarScale switch
            {
                < 0.85 => "Thin",
                < 1.1 => "Standard",
                < 1.28 => "Roomy",
                _ => "Large",
            };
            return $"{name} · {height:0} px";
        }
    }

    public bool BlurAuto
    {
        get => _settings.BackdropBlur == BackdropBlur.Auto;
        set { if (value) SetBlur(BackdropBlur.Auto); }
    }

    public bool BlurNone
    {
        get => _settings.BackdropBlur == BackdropBlur.None;
        set { if (value) SetBlur(BackdropBlur.None); }
    }

    public bool BlurSubtle
    {
        get => _settings.BackdropBlur == BackdropBlur.Subtle;
        set { if (value) SetBlur(BackdropBlur.Subtle); }
    }

    public bool BlurFull
    {
        get => _settings.BackdropBlur == BackdropBlur.Full;
        set { if (value) SetBlur(BackdropBlur.Full); }
    }

    private void SetBlur(BackdropBlur blur)
    {
        if (_settings.BackdropBlur == blur) return;
        _settings.BackdropBlur = blur;
        Persist();
        NotifyAppearance();
        OnPropertyChanged(nameof(BlurAuto));
        OnPropertyChanged(nameof(BlurNone));
        OnPropertyChanged(nameof(BlurSubtle));
        OnPropertyChanged(nameof(BlurFull));
    }

    public bool PositionBottom
    {
        get => _settings.BarPosition == BarPosition.BottomCenter;
        set { if (value) SetPosition(BarPosition.BottomCenter); }
    }

    public bool PositionNearCaret
    {
        get => _settings.BarPosition == BarPosition.NearCaret;
        set { if (value) SetPosition(BarPosition.NearCaret); }
    }

    public bool PositionTop
    {
        get => _settings.BarPosition == BarPosition.TopCenter;
        set { if (value) SetPosition(BarPosition.TopCenter); }
    }

    private void SetPosition(BarPosition position)
    {
        if (_settings.BarPosition == position) return;
        _settings.BarPosition = position;
        Persist();
        OnPropertyChanged(nameof(PositionBottom));
        OnPropertyChanged(nameof(PositionNearCaret));
        OnPropertyChanged(nameof(PositionTop));
    }

    public bool PersistentBar
    {
        get => _settings.PersistentBar;
        set
        {
            if (_settings.PersistentBar == value) return;
            _settings.PersistentBar = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public bool AutocorrectEnabled
    {
        get => _settings.AutocorrectEnabled;
        set
        {
            if (_settings.AutocorrectEnabled == value) return;
            _settings.AutocorrectEnabled = value;
            Persist();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _settings.StartWithWindows;
        set
        {
            if (_settings.StartWithWindows == value) return;
            _settings.StartWithWindows = value;
            AutostartManager.SetEnabled(value, _executablePath);
            Persist();
            OnPropertyChanged();
        }
    }

    public double MinMotionSpeed => AppSettings.MinMotionSpeed;
    public double MaxMotionSpeed => AppSettings.MaxMotionSpeed;

    public double MotionSpeed
    {
        get => _settings.MotionSpeed;
        set
        {
            if (Math.Abs(_settings.MotionSpeed - value) < 0.001) return;
            _settings.MotionSpeed = value;
            Persist();
            NotifyAppearance();
            OnPropertyChanged();
            OnPropertyChanged(nameof(MotionSpeedLabel));
        }
    }

    public string MotionSpeedLabel
    {
        get
        {
            var profile = MotionProfile.ForSpeed(_settings.MotionSpeed);

            // The far end of the slider turns motion off outright, so it gets a name rather than a duration.
            // Reporting "0 ms" there would be technically true and completely unhelpful.
            if (profile.IsInstant) return "Off · no animation";

            var name = _settings.MotionSpeed switch
            {
                < 0.8 => "Relaxed",
                < 1.2 => "Default",
                < 1.8 => "Quick",
                _ => "Snappy",
            };

            return $"{name} · {profile.LensSeconds * 1000:0} ms";
        }
    }

    /// <summary>Warns in the UI when Windows itself is overriding the glass, so the settings don't look broken.</summary>
    public bool TransparencyDisabledBySystem => !SystemAppearance.UseGlass;

    /// <summary>Warns when Windows' own "animation effects" switch is off, which overrides the speed slider.</summary>
    public bool MotionDisabledBySystem => !SystemAppearance.UseMotion;

    private void Persist() => _store.Save(_settings);

    private void NotifyAppearance() => _onAppearanceChanged();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
