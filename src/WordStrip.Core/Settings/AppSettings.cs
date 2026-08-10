namespace WordStrip.Core.Settings;

/// <summary>User-configurable behavior, persisted to disk. Mutable POCO — bind directly or copy out as needed.</summary>
public sealed class AppSettings
{
    public const int MinSuggestionCount = 3;
    public const int MaxSuggestionCount = 7;

    /// <summary>
    /// Tint bounds. The floor is deliberately not 0: Apple's guidance is that thinner material preserves
    /// context while thicker material preserves legibility, and below roughly 15% the text on the bar stops
    /// meeting contrast requirements over bright backgrounds.
    /// </summary>
    public const double MinGlassTint = 0.15;
    public const double MaxGlassTint = 0.95;

    /// <summary>Bar thickness bounds, as a multiplier on the standard metrics.</summary>
    public const double MinBarScale = 0.7;
    public const double MaxBarScale = 1.4;

    /// <summary>Animation speed bounds. Higher is faster; every duration is divided by this.</summary>
    public const double MinMotionSpeed = 0.5;
    public const double MaxMotionSpeed = 2.5;

    private int _suggestionCount = 4;
    private double _glassTint = 0.62;
    private double _barScale = 1.0;
    private double _motionSpeed = 1.0;

    /// <summary>How many candidates to show on the strip. Clamped to [3, 7].</summary>
    public int SuggestionCount
    {
        get => _suggestionCount;
        set => _suggestionCount = Math.Clamp(value, MinSuggestionCount, MaxSuggestionCount);
    }

    /// <summary>
    /// How thick the glass material reads, [0.15, 0.95]. Higher is more opaque, giving text stronger
    /// contrast; lower is more translucent, letting more of the window behind show through.
    /// </summary>
    public double GlassTint
    {
        get => _glassTint;
        set => _glassTint = Math.Clamp(value, MinGlassTint, MaxGlassTint);
    }

    /// <summary>
    /// How thick the strip is, [0.7, 1.4]. Scales text, padding and corner radii together so the proportions
    /// hold. A thin strip is worth having when the bar follows the caret, where it sits over the text the
    /// user is reading and every pixel of height is in the way.
    /// </summary>
    public double BarScale
    {
        get => _barScale;
        set => _barScale = Math.Clamp(value, MinBarScale, MaxBarScale);
    }

    /// <summary>
    /// How quickly the strip animates, [0.5, 2.5]. Higher is faster. The baseline is already brisk — this
    /// exists because how much motion feels right while typing is genuinely a matter of taste.
    /// </summary>
    public double MotionSpeed
    {
        get => _motionSpeed;
        set => _motionSpeed = Math.Clamp(value, MinMotionSpeed, MaxMotionSpeed);
    }

    /// <summary>Which visual personality the bar wears. Purely presentational — behaviour is identical in all.</summary>
    public BarTheme Theme { get; set; } = BarTheme.FluentAcrylic;

    /// <summary>
    /// Backdrop blur override. <see cref="BackdropBlur.Auto"/> defers to whatever the chosen theme was
    /// designed around, which is what keeps a theme looking like itself unless the user deliberately says
    /// otherwise.
    /// </summary>
    public BackdropBlur BackdropBlur { get; set; } = BackdropBlur.Auto;

    public BarPosition BarPosition { get; set; } = BarPosition.BottomCenter;

    public bool AutocorrectEnabled { get; set; } = true;

    /// <summary>
    /// Whether the strip stays on screen between words, showing common words when nothing is part-typed,
    /// the way a phone keyboard's suggestion row does. Off restores the original behaviour, where the strip
    /// appears for the duration of each word and vanishes the moment it is committed.
    ///
    /// <para>Defaults to on: the strip reappearing and disappearing on every space is the thing that reads
    /// as flicker while typing at speed, and a row that simply stays put is calmer to type alongside.</para>
    /// </summary>
    public bool PersistentBar { get; set; } = true;

    /// <summary>
    /// Whether WordStrip learns from what you type — personal word, pair and triple counts, kept on this
    /// machine and used to bias suggestions toward your own writing.
    ///
    /// <para>Defaults to <b>off</b>. Everything else in this file changes how the app looks or behaves;
    /// this one changes what it records about the person using it, and that is not a reasonable thing to
    /// switch on for someone without asking. The feature is worth having, which is why it exists — but
    /// opting in is the user's decision to make, not a default to be discovered later.</para>
    /// </summary>
    public bool PersonalLearningEnabled { get; set; }

    /// <summary>
    /// Whether an emoji may take one of the bar's slots when it clearly matches the word being typed, the
    /// way a phone keyboard offers one. On by default: at most one appears, only on an unambiguous match,
    /// and it is easy to ignore — whereas someone who wants it and has to go and find a switch mostly never
    /// discovers the feature exists.
    /// </summary>
    public bool EmojiSuggestionsEnabled { get; set; } = true;

    /// <summary>
    /// Whether the bar may offer several words as one suggestion ("forward to", "let me know"). On by
    /// default; turning it off restores strictly one word per slot for anyone who finds phrases presumptuous.
    /// </summary>
    public bool PhraseSuggestionsEnabled { get; set; } = true;

    public bool StartWithWindows { get; set; }
}
