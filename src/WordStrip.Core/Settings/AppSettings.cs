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

    /// <summary>
    /// Fixed-width bounds, as a fraction of the screen's work area.
    ///
    /// <para>A fraction rather than a pixel count so the choice survives being carried to a different
    /// display. The floor is not lower because a strip narrower than a fifth of the screen cannot hold
    /// several words without clipping most of them; the ceiling stops short of the full width because a
    /// strip running edge to edge stops reading as an overlay.</para>
    /// </summary>
    public const double MinBarWidthFraction = 0.20;
    public const double MaxBarWidthFraction = 0.90;

    private int _suggestionCount = 4;
    private double _glassTint = 0.62;
    private double _barScale = 1.0;
    private double _motionSpeed = 1.0;
    private double _barWidthFraction = 0.42;

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
    /// Whether the strip adapts its palette to the backdrop, or is pinned to light or dark.
    ///
    /// <para>Defaults to <see cref="AppearanceMode.Auto"/>, which is the behaviour that already existed and
    /// is the better one over a backdrop that holds still. Anyone who spends their day in a browser will
    /// want to pin it — see the remarks on the enum.</para>
    /// </summary>
    public AppearanceMode AppearanceMode { get; set; } = AppearanceMode.Auto;

    /// <summary>
    /// Whether the strip keeps one geometry regardless of what is on it.
    ///
    /// <para>On by default. The strip's width is then fixed by how many slots it has, not by the words in
    /// them, so typing changes what the bar says and never its shape. Off sizes the strip to its content,
    /// which wastes no space but means it resizes as the words change — kept for anyone who prefers that,
    /// but a strip that moves on every keystroke is exactly what a writing aid should not do.</para>
    /// </summary>
    public bool FixedBarWidth { get; set; } = true;

    /// <summary>
    /// How wide the strip is when <see cref="FixedBarWidth"/> is on, as a fraction of the work area.
    /// Clamped to [0.20, 0.90]. Ignored entirely when the strip is sizing to its content.
    /// </summary>
    public double BarWidthFraction
    {
        get => _barWidthFraction;
        set => _barWidthFraction = Math.Clamp(value, MinBarWidthFraction, MaxBarWidthFraction);
    }

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

    /// <summary>
    /// Whether the downloaded neural model may reorder suggestions.
    ///
    /// <para>Off by default and meaningless until a model is downloaded, which is itself an explicit act.
    /// Separate from the download so the feature can be switched off without throwing away 227 MB, and so
    /// turning it off is instant rather than a decision about disk space.</para>
    /// </summary>
    public bool NeuralRerankingEnabled { get; set; }

    public bool StartWithWindows { get; set; }

    // --- Interaction model -----------------------------------------------------------------------------

    public const int MinCompletionPrefixLength = 2;
    public const int MaxCompletionPrefixLength = 6;
    public const int MinCycleWindowMs = 400;
    public const int MaxCycleWindowMs = 2500;

    private int _completionMinPrefixLength = 3;
    private double _completionMinConfidence = 0.6;
    private double _completionMinScoreMargin = 0.25;
    private int _predictionCycleWindowMs = 900;
    private double _rankingHysteresis = 0.35;

    /// <summary>
    /// Whether Space and closing punctuation finish a partly typed word with its strongest completion —
    /// "looki" then Space gives "looking ". Only ever fires when the completion is unambiguous; see
    /// <c>CompletionPolicy</c> for every condition. On by default because it is the change that lets the
    /// bar be used without being operated.
    /// </summary>
    public bool CompleteOnSpace { get; set; } = true;

    /// <summary>
    /// Shortest partial word Space may complete, [2, 6]. Two letters match almost anything, so the floor is a
    /// safety margin rather than a preference.
    /// </summary>
    public int CompletionMinPrefixLength
    {
        get => _completionMinPrefixLength;
        set => _completionMinPrefixLength = Math.Clamp(value, MinCompletionPrefixLength, MaxCompletionPrefixLength);
    }

    /// <summary>
    /// How much of the candidates' combined likelihood the top completion must hold before Space may commit
    /// it, [0.3, 0.99]. Computed from ranking scores, which are log-scaled, so 0.6 means "clearly the answer"
    /// rather than "narrowly ahead".
    /// </summary>
    public double CompletionMinConfidence
    {
        get => _completionMinConfidence;
        set => _completionMinConfidence = Math.Clamp(value, 0.3, 0.99);
    }

    /// <summary>
    /// Minimum lead, in ranking-score units, the top completion must have over the runner-up, [0, 5]. Score
    /// units are roughly log₁₀ of frequency, so 0.25 is a lead of about 1.8×.
    /// </summary>
    public double CompletionMinScoreMargin
    {
        get => _completionMinScoreMargin;
        set => _completionMinScoreMargin = Math.Clamp(value, 0, 5);
    }

    /// <summary>
    /// How long after Tab inserts a prediction a further Tab replaces it with the next candidate rather than
    /// inserting another word, in milliseconds, [400, 2500].
    /// </summary>
    public int PredictionCycleWindowMs
    {
        get => _predictionCycleWindowMs;
        set => _predictionCycleWindowMs = Math.Clamp(value, MinCycleWindowMs, MaxCycleWindowMs);
    }

    /// <summary>
    /// Score lead a candidate needs before it may overtake one already on screen, [0, 5]. Purely about visual
    /// stability — the model's scores are untouched; only the order the bar shows them in resists churn.
    /// </summary>
    public double RankingHysteresis
    {
        get => _rankingHysteresis;
        set => _rankingHysteresis = Math.Clamp(value, 0, 5);
    }
}
