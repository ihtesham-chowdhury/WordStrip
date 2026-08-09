using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Prediction;

/// <summary>
/// Orchestrates candidate generation and hands the result to a ranker. Three strategies:
///   - Live completion: prefix match against the dictionary while a word is still being typed.
///   - Next-word prediction: the n-gram model, once a word has been finished and the question becomes what
///     follows rather than what completes.
///   - Autocorrection: fuzzy (edit-distance) match against the dictionary once a word boundary is hit,
///     for words that aren't in the dictionary as typed.
///
/// <para>Entirely offline: no personalization, no network. This class deliberately only <em>gathers</em>
/// candidates and delegates ordering to <see cref="ICandidateRanker"/> — which is what let Phase 2's
/// contextual signal arrive as <see cref="ContextualRanker"/> without this class learning anything new about
/// how candidates are scored.</para>
///
/// <para><b>The two modes are separate on purpose.</b> While there is a partial word the user has told us
/// something concrete and completion leads; once the word is finished there is nothing to complete and the
/// language model leads. Blending the two would mean offering words that neither match what is being typed
/// nor follow from what came before.</para>
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
    private readonly NGramLanguageModel _languageModel;

    /// <param name="languageModel">
    /// Contextual model. Defaults to an empty one, which backs off to plain word frequency for everything —
    /// so the engine behaves exactly as it did before Phase 2 when no model files are present.
    /// </param>
    /// <param name="ranker">
    /// Defaults to <see cref="ContextualRanker"/> over whatever model was supplied. With an empty model that
    /// scores identically to <see cref="FrequencyRanker"/>, since the context bonus is zero when the model
    /// knows nothing.
    /// </param>
    public PredictionEngine(
        FrequencyDictionary dictionary,
        SymSpellIndex fuzzyIndex,
        PrefixIndex? prefixIndex = null,
        ICandidateRanker? ranker = null,
        NGramLanguageModel? languageModel = null)
    {
        _dictionary = dictionary;
        _fuzzyIndex = fuzzyIndex;
        _prefixIndex = prefixIndex ?? PrefixIndex.Build(dictionary);
        _languageModel = languageModel ?? NGramLanguageModel.Empty(dictionary);
        _ranker = ranker ?? new ContextualRanker(_languageModel);
    }

    /// <summary>Resource name of the dictionary compiled into the app assembly.</summary>
    private const string EmbeddedDictionaryName = "WordStrip.dictionary.en.txt";

    /// <summary>
    /// Builds the engine from the dictionary at <paramref name="dictionaryFilePath"/> if that file exists,
    /// otherwise from the copy embedded in <paramref name="embeddedResourceAssembly"/>. The file wins so a
    /// dictionary can be swapped without a rebuild; the embedded copy is what makes the single-file build
    /// work when nothing but the exe was copied.
    /// </summary>
    /// <param name="nGramDirectory">
    /// Folder holding <c>ngram-2.txt</c> / <c>ngram-3.txt</c>. Loose files win over the embedded copies, on
    /// the same reasoning as the dictionary: the model stays swappable without a rebuild, while the embedded
    /// copies keep the portable single-file build working when only the exe was copied.
    /// </param>
    public static PredictionEngine LoadDefault(
        string dictionaryFilePath,
        System.Reflection.Assembly embeddedResourceAssembly,
        int maxVocabularySize = 60_000,
        int maxEditDistance = 2,
        string? nGramDirectory = null)
    {
        var dictionary = File.Exists(dictionaryFilePath)
            ? FrequencyDictionary.LoadFromFile(dictionaryFilePath, maxVocabularySize)
            : LoadEmbedded(embeddedResourceAssembly, maxVocabularySize);

        var languageModel = NGramLanguageModel.Load(
            nGramDirectory ?? string.Empty, embeddedResourceAssembly, dictionary);

        return new PredictionEngine(
            dictionary,
            SymSpellIndex.Build(dictionary, maxEditDistance),
            languageModel: languageModel);
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
    public IReadOnlyList<Suggestion> GetLiveSuggestions(string partialWord, int maxResults) =>
        GetLiveSuggestions(partialWord, maxResults, PredictionContext.Empty);

    /// <summary>
    /// Suggestions for the strip while a word is still being typed, reordered by what came before it.
    ///
    /// <para>Context never changes <em>which</em> words are offered here, only their order. The candidate
    /// list is still everything that completes or plausibly repairs what has been typed — anything else
    /// would mean showing a word that does not match the letters on screen.</para>
    /// </summary>
    public IReadOnlyList<Suggestion> GetLiveSuggestions(string partialWord, int maxResults, PredictionContext context)
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

        return _ranker.Rank(new RankingContext(prefix, context), candidates, maxResults);
    }

    /// <summary>
    /// Common words to show when there is no in-progress word.
    ///
    /// <para>Purely frequency-based, with no notion of context. Kept because it is the honest answer when
    /// there is genuinely no context to work from, and because it is the behaviour the bar falls back to if
    /// the model files are missing. <see cref="GetNextWords"/> is the contextual replacement.</para>
    /// </summary>
    public IReadOnlyList<Suggestion> GetFrequentWords(int maxResults) =>
        maxResults <= 0 ? Array.Empty<Suggestion>() : _prefixIndex.MostFrequent(maxResults);

    /// <summary>
    /// What is likely to come next, given the words before the caret. This is what the strip shows between
    /// words, and the seam Phase 2 was built to fill: it turns "the commonest words in English" into "the
    /// words that usually follow what you just typed".
    ///
    /// <para>Candidates come from the language model's own backoff chain, so a context it has never seen
    /// still yields common words rather than an empty bar. They are tagged
    /// <see cref="SuggestionSource.FrequentWord"/> because from the ranker's point of view that is what they
    /// are — offered with no prefix to match against. <see cref="ContextualRanker"/> is what then separates
    /// a genuinely predicted word from mere filler, via the bonus it attaches to real n-gram evidence.</para>
    /// </summary>
    public IReadOnlyList<Suggestion> GetNextWords(PredictionContext context, int maxResults)
    {
        if (maxResults <= 0) return Array.Empty<Suggestion>();

        // Gather wider than the display cap so the ranker has genuine choice, matching how completion works.
        var predictions = _languageModel.GetNextWordCandidates(context, Math.Max(maxResults, CandidatePoolSize / 4));
        if (predictions.Count == 0) return GetFrequentWords(maxResults);

        var candidates = new List<Suggestion>(predictions.Count);
        foreach (var prediction in predictions)
        {
            candidates.Add(new Suggestion(
                prediction.Word,
                _dictionary.GetFrequency(prediction.Word),
                EditDistance: 0,
                SuggestionSource.FrequentWord));
        }

        return _ranker.Rank(new RankingContext(string.Empty, context), candidates, maxResults);
    }

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
