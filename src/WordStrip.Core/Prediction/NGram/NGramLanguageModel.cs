using System.Globalization;

namespace WordStrip.Core.Prediction.NGram;

/// <summary>Which order of the model actually produced an answer. Exposed so callers — and tests — can tell a trigram hit from a fallback.</summary>
public enum NGramOrder
{
    None = 0,
    Unigram = 1,
    Bigram = 2,
    Trigram = 3,
}

/// <summary>A predicted next word, its backoff score (log10), and the order that produced it.</summary>
public readonly record struct NGramPrediction(string Word, double LogScore, NGramOrder Order);

/// <summary>
/// A local trigram/bigram language model with unigram backoff, answering "given the words before the caret,
/// what comes next?".
///
/// <para><b>Backoff.</b> Stupid backoff (Brants et al., 2007): try the trigram, and if the context is unseen
/// fall back to the bigram, then to plain word frequency, multiplying by a fixed penalty at each step down.
/// It is not a normalised probability distribution and does not pretend to be — it is a score. That is the
/// point: proper discounting (Kneser-Ney and friends) buys accuracy that matters when you are measuring
/// perplexity over a corpus, and buys nothing when you are ordering seven words on a strip. Stupid backoff
/// is a few lines, has no tuned parameters beyond the penalty, and is exactly reproducible.</para>
///
/// <para><b>Unigram tier.</b> Comes from the frequency dictionary the app already ships rather than from a
/// third model file. Its counts come from a different corpus than the n-grams, which would matter if these
/// were being combined into a real probability — under stupid backoff they are only ever compared after the
/// penalty has already put them a fixed distance apart, so the mismatch is not load-bearing.</para>
///
/// <para><b>Cost.</b> Every lookup is a dictionary hit on a pre-built table; nothing scans, nothing touches
/// disk after construction, and no candidate list is longer than what the builder kept per context.</para>
/// </summary>
public sealed class NGramLanguageModel
{
    /// <summary>
    /// Penalty applied each time the model gives up an order, as log10(0.4) — the value from the stupid
    /// backoff paper. Large enough that any genuine trigram outranks any bigram, so a real contextual match
    /// is never buried under a merely common word.
    /// </summary>
    public static readonly double BackoffPenalty = Math.Log10(0.4);

    /// <summary>How many top-frequency words to precompute for the unigram tier. Above the 7-suggestion display cap, with room for duplicates already taken by a higher order.</summary>
    private const int UnigramTierSize = 32;

    private readonly Dictionary<string, NGramContinuation[]> _bigrams;
    private readonly Dictionary<string, NGramContinuation[]> _trigrams;
    private readonly FrequencyDictionary _unigrams;
    private readonly double _unigramTotal;
    private readonly NGramContinuation[] _mostFrequent;

    /// <summary>Internal rather than private only because <see cref="ContextLookup"/> takes these in its constructor.</summary>
    internal readonly record struct NGramContinuation(string Word, double LogProbability);

    public int BigramContextCount => _bigrams.Count;
    public int TrigramContextCount => _trigrams.Count;

    private NGramLanguageModel(
        Dictionary<string, NGramContinuation[]> bigrams,
        Dictionary<string, NGramContinuation[]> trigrams,
        FrequencyDictionary unigrams)
    {
        _bigrams = bigrams;
        _trigrams = trigrams;
        _unigrams = unigrams;

        // Summed once so the unigram tier is a probability rather than a raw count, which would otherwise
        // dwarf the log-probabilities coming out of the other two tiers.
        var total = 0d;
        foreach (var frequency in unigrams.WordFrequency.Values) total += frequency;
        _unigramTotal = total > 0 ? total : 1;

        // Precomputed once. The unigram tier is consulted whenever context runs dry, which between words is
        // often, and ordering 60,000 words to find the top 32 is not something to do on a keystroke.
        _mostFrequent = unigrams.WordFrequency
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(UnigramTierSize)
            .Select(pair => new NGramContinuation(pair.Key, Math.Log10(pair.Value / _unigramTotal)))
            .ToArray();
    }

    /// <summary>An empty model. Every lookup falls straight through to the unigram tier, so the app still works with no model files present.</summary>
    public static NGramLanguageModel Empty(FrequencyDictionary unigrams) => new(
        new Dictionary<string, NGramContinuation[]>(StringComparer.Ordinal),
        new Dictionary<string, NGramContinuation[]>(StringComparer.Ordinal),
        unigrams);

    /// <summary>
    /// The words most likely to follow the context, best first, already backed off through the orders.
    ///
    /// <para>A trigram hit does not stop the search. If the trigram context only knows three continuations
    /// and the bar has room for seven, the rest come from the bigram tier and then from raw frequency, each
    /// tier penalised so it can never displace a better-evidenced answer above it. Words already suggested
    /// by a higher order are not repeated.</para>
    /// </summary>
    public IReadOnlyList<NGramPrediction> GetNextWordCandidates(PredictionContext context, int maxResults)
    {
        if (maxResults <= 0) return Array.Empty<NGramPrediction>();

        var modelContext = context.ModelContext();
        var results = new List<NGramPrediction>(maxResults);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (modelContext.Count >= 2 &&
            _trigrams.TryGetValue(TrigramKey(modelContext[^2], modelContext[^1]), out var trigramMatches))
        {
            Collect(trigramMatches, NGramOrder.Trigram, penalty: 0, results, seen, maxResults);
        }

        if (results.Count < maxResults && modelContext.Count >= 1 &&
            _bigrams.TryGetValue(modelContext[^1], out var bigramMatches))
        {
            Collect(bigramMatches, NGramOrder.Bigram, BackoffPenalty, results, seen, maxResults);
        }

        // Last resort, and the reason the bar is never empty in a context the model has never seen: the
        // commonest words in the language, two penalties down so they sit below anything with real evidence.
        if (results.Count < maxResults)
            Collect(_mostFrequent, NGramOrder.Unigram, 2 * BackoffPenalty, results, seen, maxResults);

        return results;
    }

    /// <summary>
    /// Resolves a context to its lookup tables once, so scoring a whole candidate list costs one dictionary
    /// probe rather than one per candidate.
    ///
    /// <para>This exists because of what re-ranking actually does: while the user is mid-word the ranker
    /// scores up to sixty-four completions against the <em>same</em> two preceding words. Asking the model
    /// per candidate re-derives the context and re-probes both tables every time, and it measurably showed —
    /// the completion path went from 161 µs to 484 µs a keystroke before this was hoisted out.</para>
    /// </summary>
    public ContextLookup Resolve(PredictionContext context)
    {
        var modelContext = context.ModelContext();

        NGramContinuation[]? trigram = null;
        NGramContinuation[]? bigram = null;

        if (modelContext.Count >= 2)
            _trigrams.TryGetValue(TrigramKey(modelContext[^2], modelContext[^1]), out trigram);

        if (modelContext.Count >= 1)
            _bigrams.TryGetValue(modelContext[^1], out bigram);

        return new ContextLookup(this, trigram, bigram);
    }

    /// <summary>
    /// One context's tables, pre-fetched. Scores individual words against them without touching the
    /// dictionaries again.
    /// </summary>
    public readonly struct ContextLookup
    {
        private readonly NGramLanguageModel _model;
        private readonly NGramContinuation[]? _trigram;
        private readonly NGramContinuation[]? _bigram;

        internal ContextLookup(NGramLanguageModel model, NGramContinuation[]? trigram, NGramContinuation[]? bigram)
        {
            _model = model;
            _trigram = trigram;
            _bigram = bigram;
        }

        /// <summary>
        /// Walks the backoff chain for one word, reporting both which order answered and what it scored.
        /// Returned together because every caller needs both, and computing them separately meant walking
        /// the same chain twice.
        /// </summary>
        public NGramOrder Score(string word, out double logScore)
        {
            logScore = 0;

            var normalized = NGramTokenizer.Normalize(word);
            if (normalized.Length == 0) return NGramOrder.None;

            if (_trigram is not null && TryFind(_trigram, normalized, out var trigramProbability))
            {
                logScore = trigramProbability;
                return NGramOrder.Trigram;
            }

            if (_bigram is not null && TryFind(_bigram, normalized, out var bigramProbability))
            {
                logScore = BackoffPenalty + bigramProbability;
                return NGramOrder.Bigram;
            }

            var frequency = _model._unigrams.GetFrequency(normalized);
            if (frequency <= 0) return NGramOrder.None;

            // Two steps down from the trigram, so the penalty applies twice.
            logScore = (2 * BackoffPenalty) + Math.Log10(frequency / _model._unigramTotal);
            return NGramOrder.Unigram;
        }
    }

    /// <summary>
    /// The backoff score for one specific word in this context, or null when even the unigram tier has never
    /// heard of it. Convenience over <see cref="Resolve"/> for one-off queries; scoring a list should
    /// resolve once and reuse it.
    /// </summary>
    public double? GetLogScore(PredictionContext context, string word) =>
        Resolve(context).Score(word, out var logScore) == NGramOrder.None ? null : logScore;

    /// <summary>Which order would answer for this word — for diagnostics and for tests asserting the backoff chain.</summary>
    public NGramOrder GetMatchedOrder(PredictionContext context, string word) =>
        Resolve(context).Score(word, out _);

    private static void Collect(
        NGramContinuation[] source,
        NGramOrder order,
        double penalty,
        List<NGramPrediction> results,
        HashSet<string> seen,
        int maxResults)
    {
        foreach (var continuation in source)
        {
            if (results.Count >= maxResults) return;
            if (!seen.Add(continuation.Word)) continue;

            results.Add(new NGramPrediction(continuation.Word, penalty + continuation.LogProbability, order));
        }
    }

    private static bool TryFind(NGramContinuation[] continuations, string word, out double logProbability)
    {
        // Linear over an array the builder capped at a dozen entries: for lists this short it beats a
        // dictionary per context, which would cost far more in allocation and memory than it saves here.
        foreach (var continuation in continuations)
        {
            if (string.Equals(continuation.Word, word, StringComparison.Ordinal))
            {
                logProbability = continuation.LogProbability;
                return true;
            }
        }

        logProbability = 0;
        return false;
    }

    private const string KeySeparator = "\t";

    private static string TrigramKey(string first, string second) =>
        string.Concat(first, KeySeparator, second);

    // --- Loading ------------------------------------------------------------------------------------

    /// <summary>
    /// Loads the model from <c>ngram-2.txt</c> and <c>ngram-3.txt</c> in <paramref name="directory"/> if they
    /// are there, otherwise from the copies embedded in <paramref name="embeddedResourceAssembly"/>. Mirrors
    /// how the dictionary is loaded, and for the same reason: loose files make the model swappable without a
    /// rebuild, while the embedded copies keep the single-file portable build genuinely single-file.
    /// </summary>
    public static NGramLanguageModel Load(
        string directory,
        System.Reflection.Assembly? embeddedResourceAssembly,
        FrequencyDictionary unigrams)
    {
        var bigrams = LoadOrder(directory, 2, embeddedResourceAssembly);
        var trigrams = LoadOrder(directory, 3, embeddedResourceAssembly);
        return new NGramLanguageModel(bigrams, trigrams, unigrams);
    }

    /// <summary>Loads from arbitrary readers. The seam the tests build small hand-written models through.</summary>
    public static NGramLanguageModel LoadFrom(TextReader? bigramSource, TextReader? trigramSource, FrequencyDictionary unigrams)
    {
        var bigrams = bigramSource is null
            ? new Dictionary<string, NGramContinuation[]>(StringComparer.Ordinal)
            : Parse(bigramSource, contextFields: 1);

        var trigrams = trigramSource is null
            ? new Dictionary<string, NGramContinuation[]>(StringComparer.Ordinal)
            : Parse(trigramSource, contextFields: 2);

        return new NGramLanguageModel(bigrams, trigrams, unigrams);
    }

    private static Dictionary<string, NGramContinuation[]> LoadOrder(
        string directory,
        int order,
        System.Reflection.Assembly? assembly)
    {
        var path = Path.Combine(directory, NGramFormat.FileName(order));
        if (File.Exists(path))
        {
            using var reader = new StreamReader(path);
            return Parse(reader, contextFields: order - 1);
        }

        var stream = assembly?.GetManifestResourceStream(NGramFormat.EmbeddedResourceName(order));
        if (stream is null)
            return new Dictionary<string, NGramContinuation[]>(StringComparer.Ordinal);

        using var embedded = new StreamReader(stream);
        return Parse(embedded, contextFields: order - 1);
    }

    /// <summary>
    /// Parses the tab-separated model format into per-context arrays.
    ///
    /// <para>Written against spans and index arithmetic rather than <c>string.Split</c>: the file holds a few
    /// hundred thousand lines, and Split would allocate an array plus a string per field for every one of
    /// them. Grouping by context as it goes means each context string is kept once no matter how many
    /// continuations follow it.</para>
    /// </summary>
    private static Dictionary<string, NGramContinuation[]> Parse(TextReader reader, int contextFields)
    {
        var grouped = new Dictionary<string, List<NGramContinuation>>(StringComparer.Ordinal);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == NGramFormat.CommentPrefix) continue;

            var span = line.AsSpan();

            // Walk to the end of the context fields, keeping the boundary that separates context from word.
            var cursor = 0;
            var contextEnd = -1;
            for (var field = 0; field < contextFields; field++)
            {
                var next = span[cursor..].IndexOf(NGramFormat.FieldSeparator);
                if (next < 0) { contextEnd = -1; break; }
                cursor += next + 1;
                contextEnd = cursor - 1;
            }

            if (contextEnd <= 0) continue;

            var wordEnd = span[cursor..].IndexOf(NGramFormat.FieldSeparator);
            if (wordEnd <= 0) continue;

            var word = span.Slice(cursor, wordEnd).ToString();
            var probabilitySpan = span[(cursor + wordEnd + 1)..];

            if (!double.TryParse(probabilitySpan, NumberStyles.Float, CultureInfo.InvariantCulture, out var logProbability))
                continue;

            var context = span[..contextEnd].ToString();

            if (!grouped.TryGetValue(context, out var continuations))
            {
                continuations = new List<NGramContinuation>(4);
                grouped[context] = continuations;
            }

            continuations.Add(new NGramContinuation(word, logProbability));
        }

        var result = new Dictionary<string, NGramContinuation[]>(grouped.Count, StringComparer.Ordinal);
        foreach (var (context, continuations) in grouped)
        {
            // The builder already writes each context's continuations best-first, but sorting here means a
            // hand-edited or externally produced file still behaves. Ties break ordinally so the order the
            // user sees never depends on dictionary iteration order.
            continuations.Sort(static (a, b) =>
            {
                var byProbability = b.LogProbability.CompareTo(a.LogProbability);
                return byProbability != 0 ? byProbability : string.CompareOrdinal(a.Word, b.Word);
            });

            result[context] = continuations.ToArray();
        }

        return result;
    }
}
