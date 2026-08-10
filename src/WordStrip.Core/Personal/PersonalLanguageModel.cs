using System.Text.Json;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Personal;

/// <summary>
/// What the user's own writing says about what they write next: personal unigram, bigram and trigram counts,
/// learned from words as they are committed.
///
/// <para><b>Statistics, not text.</b> Nothing here stores a sentence, a document, or a keystroke log. The
/// file holds counts against word sequences of at most three, which is enough to know that "british council"
/// tends to be followed by "northfield" and not enough to reconstruct anything anyone wrote. That is a
/// deliberate ceiling, not a side effect of the implementation.</para>
///
/// <para><b>Bounded, and it forgets.</b> Counts saturate and the whole model decays on a schedule, so a word
/// typed five hundred times last year fades rather than owning the bar forever, and the file cannot grow
/// without limit no matter how much the user types.</para>
///
/// <para><b>Off unless asked for.</b> The controller only feeds this when the user has switched personal
/// learning on, and never from a password field. This class does no gating of its own — it cannot see the
/// focused control — so callers must not hand it text they are not certain about.</para>
/// </summary>
public sealed class PersonalLanguageModel
{
    /// <summary>
    /// Maximum entries per order. Past this the least-used are pruned. Sized so the file stays small — a few
    /// hundred kilobytes at worst — while comfortably covering the vocabulary and phrasing one person
    /// actually reuses.
    /// </summary>
    public const int MaxEntriesPerOrder = 20_000;

    /// <summary>
    /// Ceiling on any single count. Bounds the influence of one sequence however often it is typed, which is
    /// what stops a signature block or a pasted-and-retyped phrase drowning out everything else.
    /// </summary>
    public const int MaxCount = 1_000;

    /// <summary>
    /// Every this many learned words, all counts are multiplied by <see cref="DecayFactor"/>. Recency comes
    /// out of the arithmetic rather than from storing timestamps per entry: what someone wrote recently keeps
    /// its weight simply by not having been decayed as many times.
    /// </summary>
    public const int DecayIntervalWords = 20_000;

    public const double DecayFactor = 0.9;

    /// <summary>
    /// Learned words needed before the personal model is trusted at full weight. Below this its influence
    /// ramps in linearly, so the first few sentences someone types cannot swing predictions around — the
    /// cold-start problem the phase brief calls out.
    /// </summary>
    public const int FullConfidenceWords = 2_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, int> _unigrams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _bigrams = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _trigrams = new(StringComparer.Ordinal);
    private readonly string _filePath;

    private long _wordsLearned;
    private int _wordsSinceDecay;
    private bool _dirty;

    public PersonalLanguageModel(string? filePath = null)
    {
        _filePath = filePath ?? Settings.UserDataLocation.File("personal-language-model.json");
    }

    public string FilePath => _filePath;

    /// <summary>Total words ever learned. Drives the cold-start ramp and is the honest thing to show a user asking what has been learned.</summary>
    public long WordsLearned { get { lock (_gate) return _wordsLearned; } }

    public int UnigramCount { get { lock (_gate) return _unigrams.Count; } }
    public int BigramCount { get { lock (_gate) return _bigrams.Count; } }
    public int TrigramCount { get { lock (_gate) return _trigrams.Count; } }

    public bool HasUnsavedChanges { get { lock (_gate) return _dirty; } }

    /// <summary>
    /// How far to trust this model, 0 to 1, ramping linearly to <see cref="FullConfidenceWords"/>.
    /// Multiplying the personal boost by this is what makes learning arrive gradually instead of the app
    /// abruptly behaving differently a few sentences in.
    /// </summary>
    public double Confidence
    {
        get
        {
            lock (_gate) return Math.Clamp((double)_wordsLearned / FullConfidenceWords, 0, 1);
        }
    }

    // --- Learning -----------------------------------------------------------------------------------

    /// <summary>
    /// Records one committed word together with what preceded it. <paramref name="precedingWords"/> is
    /// oldest-first, exactly as <see cref="Input.TypingSession.RecentWords"/> reports it, and may be empty at
    /// a sentence start.
    /// </summary>
    public void Learn(string word, IReadOnlyList<string> precedingWords)
    {
        var target = NGramTokenizer.Normalize(word);

        // One-letter tokens carry no useful signal and would be the bulk of what gets learned from ordinary
        // typing; skipping them keeps the store meaningful and smaller.
        if (target.Length < 2) return;

        lock (_gate)
        {
            Increment(_unigrams, target);

            var previous = LastNormalized(precedingWords, 1);
            if (previous is not null)
            {
                Increment(_bigrams, Key(previous, target));

                var beforePrevious = LastNormalized(precedingWords, 2);
                if (beforePrevious is not null)
                    Increment(_trigrams, Key(beforePrevious, previous, target));
            }

            _wordsLearned++;
            _wordsSinceDecay++;
            _dirty = true;

            if (_wordsSinceDecay >= DecayIntervalWords)
            {
                Decay();
                _wordsSinceDecay = 0;
            }
        }
    }

    private static string? LastNormalized(IReadOnlyList<string> words, int fromEnd)
    {
        if (words is null || words.Count < fromEnd) return null;

        var normalized = NGramTokenizer.Normalize(words[^fromEnd]);
        return normalized.Length == 0 ? null : normalized;
    }

    private void Increment(Dictionary<string, int> table, string key)
    {
        if (table.TryGetValue(key, out var existing))
        {
            if (existing < MaxCount) table[key] = existing + 1;
            return;
        }

        if (table.Count >= MaxEntriesPerOrder) PruneWeakest(table);
        table[key] = 1;
    }

    /// <summary>
    /// Halves the table by dropping the least-used entries. Done in bulk rather than one at a time so the
    /// cost is paid rarely: evicting a single entry per insert once full would mean an O(n) scan on every
    /// learned word forever.
    /// </summary>
    private static void PruneWeakest(Dictionary<string, int> table)
    {
        var survivors = table
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(MaxEntriesPerOrder / 2)
            .ToList();

        table.Clear();
        foreach (var (key, value) in survivors) table[key] = value;
    }

    /// <summary>
    /// Scales every count down, dropping anything that reaches zero. Old evidence fades instead of
    /// accumulating forever, and the tables shed sequences typed once and never again.
    /// </summary>
    private void Decay()
    {
        foreach (var table in new[] { _unigrams, _bigrams, _trigrams })
        {
            foreach (var key in table.Keys.ToList())
            {
                var decayed = (int)Math.Floor(table[key] * DecayFactor);
                if (decayed <= 0) table.Remove(key);
                else table[key] = decayed;
            }
        }
    }

    // --- Querying -----------------------------------------------------------------------------------

    /// <summary>
    /// How strongly the user's own writing predicts <paramref name="word"/> after <paramref name="context"/>,
    /// as a value from 0 to 1, already scaled by <see cref="Confidence"/>.
    ///
    /// <para>Conditional on the longest context with evidence — trigram, else bigram, else the word's share
    /// of everything learned — so it answers the same shape of question as the general model, from a much
    /// smaller sample.</para>
    /// </summary>
    public double GetPersonalScore(string word, IReadOnlyList<string> context)
    {
        var target = NGramTokenizer.Normalize(word);
        if (target.Length == 0) return 0;

        lock (_gate)
        {
            if (_wordsLearned == 0) return 0;

            var previous = LastNormalized(context, 1);
            var beforePrevious = LastNormalized(context, 2);

            double? probability = null;

            if (previous is not null && beforePrevious is not null &&
                _trigrams.TryGetValue(Key(beforePrevious, previous, target), out var trigramCount))
            {
                var total = TotalWithPrefix(_trigrams, Key(beforePrevious, previous));
                if (total > 0) probability = (double)trigramCount / total;
            }

            if (probability is null && previous is not null &&
                _bigrams.TryGetValue(Key(previous, target), out var bigramCount))
            {
                var total = TotalWithPrefix(_bigrams, previous);
                if (total > 0) probability = (double)bigramCount / total;
            }

            if (probability is null && _unigrams.TryGetValue(target, out var unigramCount))
                probability = (double)unigramCount / Math.Max(1, _wordsLearned);

            if (probability is null) return 0;

            return Math.Clamp(probability.Value, 0, 1) * Confidence;
        }
    }

    /// <summary>
    /// Sums the counts of every entry sharing a context prefix, i.e. the denominator for a conditional
    /// probability.
    /// </summary>
    /// <remarks>
    /// A scan of the table. Deliberately not indexed: it runs once per candidate on a store bounded to
    /// 20,000 entries, and keeping a parallel totals map correct through decay and pruning is more machinery
    /// than the microseconds justify. Revisit if the bound ever rises.
    /// </remarks>
    private static long TotalWithPrefix(Dictionary<string, int> table, string contextPrefix)
    {
        var prefix = contextPrefix + SeparatorText;
        long total = 0;

        foreach (var (key, value) in table)
        {
            if (key.StartsWith(prefix, StringComparison.Ordinal)) total += value;
        }

        return total;
    }

    public int GetUnigramCount(string word)
    {
        var key = NGramTokenizer.Normalize(word);
        lock (_gate) return key.Length > 0 && _unigrams.TryGetValue(key, out var count) ? count : 0;
    }

    public int GetBigramCount(string first, string second)
    {
        var key = Key(NGramTokenizer.Normalize(first), NGramTokenizer.Normalize(second));
        lock (_gate) return _bigrams.TryGetValue(key, out var count) ? count : 0;
    }

    public int GetTrigramCount(string first, string second, string third)
    {
        var key = Key(NGramTokenizer.Normalize(first), NGramTokenizer.Normalize(second), NGramTokenizer.Normalize(third));
        lock (_gate) return _trigrams.TryGetValue(key, out var count) ? count : 0;
    }

    // --- Privacy controls ---------------------------------------------------------------------------

    /// <summary>Forgets everything, in memory and on disk. The user-facing "clear learned data" action.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _unigrams.Clear();
            _bigrams.Clear();
            _trigrams.Clear();
            _wordsLearned = 0;
            _wordsSinceDecay = 0;
            _dirty = false;
        }

        // Deleted rather than rewritten as an empty file: "clear my data" should leave nothing behind on
        // disk, not a tidy record that there used to be something.
        try
        {
            if (File.Exists(_filePath)) File.Delete(_filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do; the in-memory model is already empty, and it will be overwritten on the
            // next save.
        }
    }

    // --- Persistence --------------------------------------------------------------------------------

    private const char Separator = '\u0001';
    private const string SeparatorText = "\u0001";

    private static string Key(string a, string b) => string.Concat(a, SeparatorText, b);
    private static string Key(string a, string b, string c) =>
        string.Concat(a, SeparatorText, b, SeparatorText, c);

    private sealed class PersistedModel
    {
        public int Version { get; set; } = 1;
        public long WordsLearned { get; set; }
        public int WordsSinceDecay { get; set; }
        public Dictionary<string, int> Unigrams { get; set; } = new();
        public Dictionary<string, int> Bigrams { get; set; } = new();
        public Dictionary<string, int> Trigrams { get; set; } = new();
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;

            var model = JsonSerializer.Deserialize<PersistedModel>(File.ReadAllText(_filePath));
            if (model is null) return;

            lock (_gate)
            {
                Copy(model.Unigrams, _unigrams);
                Copy(model.Bigrams, _bigrams);
                Copy(model.Trigrams, _trigrams);
                _wordsLearned = Math.Max(0, model.WordsLearned);
                _wordsSinceDecay = Math.Max(0, model.WordsSinceDecay);
                _dirty = false;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            // A damaged model means losing what was learned, which the app recovers from by learning again.
            // Failing to start would not be recoverable, so an unreadable file is treated as no file.
            lock (_gate)
            {
                _unigrams.Clear();
                _bigrams.Clear();
                _trigrams.Clear();
                _wordsLearned = 0;
                _wordsSinceDecay = 0;
            }
        }
    }

    private static void Copy(Dictionary<string, int> source, Dictionary<string, int> destination)
    {
        destination.Clear();
        foreach (var (key, value) in source)
        {
            if (value > 0 && destination.Count < MaxEntriesPerOrder) destination[key] = Math.Min(value, MaxCount);
        }
    }

    /// <summary>Writes only if something changed, so the periodic save costs nothing while the user is idle.</summary>
    public void SaveIfDirty()
    {
        PersistedModel snapshot;

        lock (_gate)
        {
            if (!_dirty) return;

            snapshot = new PersistedModel
            {
                WordsLearned = _wordsLearned,
                WordsSinceDecay = _wordsSinceDecay,
                Unigrams = new Dictionary<string, int>(_unigrams, StringComparer.Ordinal),
                Bigrams = new Dictionary<string, int>(_bigrams, StringComparer.Ordinal),
                Trigrams = new Dictionary<string, int>(_trigrams, StringComparer.Ordinal),
            };

            _dirty = false;
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        // Same temp-then-replace as the vocabulary store: this saves on a timer, so being interrupted
        // mid-write is a question of when rather than if.
        var temporary = _filePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));

        if (File.Exists(_filePath)) File.Replace(temporary, _filePath, destinationBackupFileName: null);
        else File.Move(temporary, _filePath);
    }
}