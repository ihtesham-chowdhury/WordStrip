namespace WordStrip.Core.Prediction;

/// <summary>
/// Immutable prefix lookup over the vocabulary.
///
/// <para>Replaces a linear scan of the whole dictionary on every keystroke. The words are held in one
/// ordinal-sorted array, so every word sharing a prefix occupies a contiguous run; a binary search finds
/// where that run starts and iteration stops as soon as the prefix stops matching. Cost goes from "touch all
/// 60,000 entries" to "log₂(60,000) ≈ 16 comparisons plus the matches themselves".</para>
///
/// <para>A trie would also work and would be marginally faster still, but it costs far more memory and code
/// for a vocabulary this size — the sorted array is the simplest thing that gives the real benefit.</para>
/// </summary>
public sealed class PrefixIndex
{
    /// <summary>How many top-frequency words to precompute. Comfortably above the 7-suggestion display cap.</summary>
    private const int FrequentWordCacheSize = 32;

    private readonly string[] _words;
    private readonly long[] _frequencies;
    private readonly IReadOnlyList<Suggestion> _mostFrequent;

    private PrefixIndex(string[] words, long[] frequencies, IReadOnlyList<Suggestion> mostFrequent)
    {
        _words = words;
        _frequencies = frequencies;
        _mostFrequent = mostFrequent;
    }

    public int Count => _words.Length;

    public static PrefixIndex Build(FrequencyDictionary dictionary)
    {
        var words = dictionary.WordFrequency.Keys.ToArray();
        Array.Sort(words, StringComparer.Ordinal);

        var frequencies = new long[words.Length];
        for (var i = 0; i < words.Length; i++)
            frequencies[i] = dictionary.GetFrequency(words[i]);

        // Precomputed once. Sorting 60,000 words to find the top handful took ~13 ms, and this list is asked
        // for every time the bar reappears between words — far too often to pay that repeatedly.
        var mostFrequent = Enumerable.Range(0, words.Length)
            .OrderByDescending(i => frequencies[i])
            .ThenBy(i => words[i], StringComparer.Ordinal)
            .Take(FrequentWordCacheSize)
            .Select(i => new Suggestion(words[i], frequencies[i], EditDistance: 0, SuggestionSource.FrequentWord))
            .ToList();

        return new PrefixIndex(words, frequencies, mostFrequent);
    }

    /// <summary>
    /// Every word starting with <paramref name="prefix"/>, in ordinal order.
    ///
    /// <para><paramref name="maxCandidates"/> bounds how many are collected, not how many are returned to the
    /// user: ranking needs a pool wider than the final list to choose from, but a one- or two-letter prefix
    /// can match thousands of words and there is no point materialising all of them.</para>
    /// </summary>
    public List<Suggestion> FindByPrefix(string prefix, int maxCandidates)
    {
        var results = new List<Suggestion>();
        if (string.IsNullOrEmpty(prefix) || maxCandidates <= 0) return results;

        var start = LowerBound(prefix);

        for (var i = start; i < _words.Length && results.Count < maxCandidates; i++)
        {
            var word = _words[i];
            if (!word.StartsWith(prefix, StringComparison.Ordinal))
                break; // sorted, so the first non-match ends the run

            var source = word.Length == prefix.Length ? SuggestionSource.ExactWord : SuggestionSource.PrefixCompletion;
            results.Add(new Suggestion(word, _frequencies[i], EditDistance: 0, source));
        }

        return results;
    }

    /// <summary>The most frequent words overall, used when there is no prefix to work from. Served from cache.</summary>
    public IReadOnlyList<Suggestion> MostFrequent(int count)
    {
        if (count <= 0) return Array.Empty<Suggestion>();
        if (count >= _mostFrequent.Count) return _mostFrequent;

        return _mostFrequent.Take(count).ToList();
    }

    /// <summary>Index of the first word that is not ordinally less than <paramref name="prefix"/>.</summary>
    private int LowerBound(string prefix)
    {
        var low = 0;
        var high = _words.Length;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (string.CompareOrdinal(_words[mid], prefix) < 0)
                low = mid + 1;
            else
                high = mid;
        }

        return low;
    }
}
