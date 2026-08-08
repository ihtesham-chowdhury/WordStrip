namespace WordStrip.Core.Prediction;

/// <summary>
/// Orchestrates candidate generation and hands the result to a ranker. Combines two strategies:
///   - Live completion: prefix match against the dictionary while a word is still being typed.
///   - Autocorrection: fuzzy (edit-distance) match against the dictionary once a word boundary is hit,
///     for words that aren't in the dictionary as typed.
///
/// <para>Entirely offline: no personalization, no context, no network. This class deliberately only
/// <em>gathers</em> candidates and delegates ordering to <see cref="ICandidateRanker"/>, so a later phase can
/// introduce contextual probability by supplying a different ranker rather than rewriting this.</para>
/// </summary>
public sealed class PredictionEngine
{
    /// <summary>
    /// How many candidates to gather before ranking. Wider than any display list so the ranker has real
    /// choice, but bounded so a one-letter prefix matching thousands of words stays cheap.
    /// </summary>
    private const int CandidatePoolSize = 64;

    private readonly FrequencyDictionary _dictionary;
    private readonly SymSpellIndex _fuzzyIndex;
    private readonly PrefixIndex _prefixIndex;
    private readonly ICandidateRanker _ranker;

    public PredictionEngine(
        FrequencyDictionary dictionary,
        SymSpellIndex fuzzyIndex,
        PrefixIndex? prefixIndex = null,
        ICandidateRanker? ranker = null)
    {
        _dictionary = dictionary;
        _fuzzyIndex = fuzzyIndex;
        _prefixIndex = prefixIndex ?? PrefixIndex.Build(dictionary);
        _ranker = ranker ?? new FrequencyRanker();
    }

    /// <summary>Resource name of the dictionary compiled into the app assembly.</summary>
    private const string EmbeddedDictionaryName = "WordStrip.dictionary.en.txt";

    /// <summary>
    /// Builds the engine from the dictionary at <paramref name="dictionaryFilePath"/> if that file exists,
    /// otherwise from the copy embedded in <paramref name="embeddedResourceAssembly"/>. The file wins so a
    /// dictionary can be swapped without a rebuild; the embedded copy is what makes the single-file build
    /// work when nothing but the exe was copied.
    /// </summary>
    public static PredictionEngine LoadDefault(
        string dictionaryFilePath,
        System.Reflection.Assembly embeddedResourceAssembly,
        int maxVocabularySize = 60_000,
        int maxEditDistance = 2)
    {
        var dictionary = File.Exists(dictionaryFilePath)
            ? FrequencyDictionary.LoadFromFile(dictionaryFilePath, maxVocabularySize)
            : LoadEmbedded(embeddedResourceAssembly, maxVocabularySize);

        return new PredictionEngine(dictionary, SymSpellIndex.Build(dictionary, maxEditDistance));
    }

    private static FrequencyDictionary LoadEmbedded(System.Reflection.Assembly assembly, int maxVocabularySize)
    {
        using var stream = assembly.GetManifestResourceStream(EmbeddedDictionaryName)
            ?? throw new InvalidOperationException(
                $"Embedded dictionary '{EmbeddedDictionaryName}' is missing from {assembly.GetName().Name}. " +
                "The build is broken — check the EmbeddedResource entry in WordStrip.App.csproj.");

        return FrequencyDictionary.LoadFromStream(stream, maxVocabularySize);
    }

    public bool IsCorrectlySpelled(string word) =>
        !string.IsNullOrEmpty(word) && _dictionary.Contains(word.ToLowerInvariant());

    /// <summary>
    /// Suggestions for the strip while a word is still being typed. Prefix completions come first; if there
    /// are too few, fuzzy matches top the list up so an early typo mid-word still produces something useful.
    /// </summary>
    public IReadOnlyList<Suggestion> GetLiveSuggestions(string partialWord, int maxResults)
    {
        if (string.IsNullOrWhiteSpace(partialWord) || maxResults <= 0)
            return Array.Empty<Suggestion>();

        var prefix = partialWord.ToLowerInvariant();
        var candidates = _prefixIndex.FindByPrefix(prefix, CandidatePoolSize);

        // Only reach for fuzzy candidates when prefix matching came up short. Short prefixes are excluded
        // because at one or two letters almost any word is within edit distance 2, which produces noise
        // rather than help.
        if (candidates.Count < maxResults && prefix.Length >= 3)
        {
            var seen = new HashSet<string>(candidates.Select(c => c.Word), StringComparer.Ordinal);
            foreach (var fuzzy in _fuzzyIndex.Lookup(prefix, CandidatePoolSize))
            {
                if (fuzzy.EditDistance == 0 || !seen.Add(fuzzy.Word)) continue;
                candidates.Add(fuzzy with { Source = SuggestionSource.FuzzyMatch });
            }
        }

        return _ranker.Rank(new RankingContext(prefix), candidates, maxResults);
    }

    /// <summary>
    /// Common words to show when there is no in-progress word — used to keep the strip present between
    /// words instead of flickering away after every space.
    ///
    /// <para>Purely frequency-based, because this phase has no notion of context. Phase 2's bigram model is
    /// what turns this from "the commonest words in English" into "the words likely to follow what you just
    /// typed", and this method is the seam where that lands.</para>
    /// </summary>
    public IReadOnlyList<Suggestion> GetFrequentWords(int maxResults) =>
        maxResults <= 0 ? Array.Empty<Suggestion>() : _prefixIndex.MostFrequent(maxResults);

    /// <summary>
    /// Best autocorrect candidate for a word that was just completed (space/punctuation typed).
    /// Returns null when the word is already correctly spelled, or no sufficiently confident
    /// correction exists — callers should not silently replace on a low-confidence guess.
    /// </summary>
    public Suggestion? GetAutocorrection(string completedWord, long minFrequencyForConfidence = 1000)
    {
        if (string.IsNullOrEmpty(completedWord) || completedWord.Length < 2)
            return null;

        var word = completedWord.ToLowerInvariant();
        if (IsCorrectlySpelled(word))
            return null;

        var matches = _fuzzyIndex.Lookup(word, 1);
        if (matches.Count == 0)
            return null;

        var best = matches[0];
        if (best.EditDistance == 0 || best.Frequency < minFrequencyForConfidence)
            return null;

        return best;
    }
}
