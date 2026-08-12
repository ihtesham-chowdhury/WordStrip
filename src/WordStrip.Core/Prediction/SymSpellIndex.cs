namespace WordStrip.Core.Prediction;

/// <summary>
/// SymSpell-style fuzzy lookup: precomputes every "delete" variant of every dictionary word (removing up
/// to <see cref="_maxEditDistance"/> characters) into an index, then finds fuzzy matches for an input word
/// by generating the same deletes for it and checking which dictionary words share a variant. This trades
/// index build time and memory for lookup speed — much faster than comparing the input against every
/// dictionary word directly, which matters since corrections need to happen live as the user types.
///
/// <para><b>Why the index looks like this.</b> The obvious structure is
/// <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c> — delete variant to the words that produce it — and
/// that is what this was. Over 60,000 words at edit distance 2 it produces about 1.8 million variants and
/// measured at <b>291 MB</b>, which was ninety per cent of the entire prediction stack. Three costs, none
/// of them the one you would guess:</para>
/// <list type="bullet">
///   <item>1.8 million <c>List&lt;string&gt;</c> objects, each with its own backing array, holding on
///   average barely more than one entry. This was the largest single cost — roughly 150 MB of container for
///   a payload that fits in an int.</item>
///   <item>1.8 million key strings, each a separate heap object with a 22-byte header before any characters.</item>
///   <item>The dictionary's own bucket and entry arrays, sized for 1.8 million items.</item>
/// </list>
///
/// <para><b>What replaced it.</b> Three flat arrays and a binary search. Variants are stored as 64-bit
/// hashes rather than strings; postings are word <em>indices</em> rather than word references, packed end to
/// end with an offset table (the compressed-sparse-row layout). Nothing is allocated per variant, so the
/// object count drops from millions to four.</para>
///
/// <para><b>Why hashing the key is safe here, and would not be everywhere.</b> A hash collision means two
/// unrelated variants land in the same bucket, so a lookup gets a candidate it did not ask for. That is
/// harmless in this design and only in this design, because <em>every</em> candidate is then verified with a
/// bounded Damerau-Levenshtein distance before it can be returned. A collision costs one wasted comparison
/// and can never produce a wrong suggestion. Remove that verification step and this becomes unsound.</para>
/// </summary>
public sealed class SymSpellIndex
{
    private readonly FrequencyDictionary _dictionary;
    private readonly int _maxEditDistance;

    /// <summary>Word index to word. Postings are indices into this.</summary>
    private readonly string[] _words;

    /// <summary>Distinct variant hashes, ascending. Binary searched on lookup.</summary>
    private readonly long[] _variantHashes;

    /// <summary>
    /// Where each variant's postings begin in <see cref="_postings"/>. One longer than
    /// <see cref="_variantHashes"/>, so a variant's run is always <c>[start[i], start[i + 1])</c> and the
    /// last entry needs no special case.
    /// </summary>
    private readonly int[] _postingStart;

    /// <summary>Word indices for every variant, packed end to end.</summary>
    private readonly int[] _postings;

    private SymSpellIndex(
        FrequencyDictionary dictionary,
        int maxEditDistance,
        string[] words,
        long[] variantHashes,
        int[] postingStart,
        int[] postings)
    {
        _dictionary = dictionary;
        _maxEditDistance = maxEditDistance;
        _words = words;
        _variantHashes = variantHashes;
        _postingStart = postingStart;
        _postings = postings;
    }

    /// <summary>Distinct delete variants indexed. Diagnostics, and the memory profile test reports against it.</summary>
    public int VariantCount => _variantHashes.Length;

    /// <summary>Total (variant, word) pairs. Always at least <see cref="VariantCount"/>.</summary>
    public int PostingCount => _postings.Length;

    public static SymSpellIndex Build(FrequencyDictionary dictionary, int maxEditDistance = 2)
    {
        var words = dictionary.WordFrequency.Keys.ToArray();

        // One pass to collect (hash, word index) pairs, then sort. Building the sorted structure directly
        // would need the count up front, and counting means generating every variant twice.
        var pairs = new List<Posting>(words.Length * 24);
        var variants = new HashSet<string>(StringComparer.Ordinal);

        for (var wordIndex = 0; wordIndex < words.Length; wordIndex++)
        {
            variants.Clear();
            CollectDeletes(words[wordIndex], maxEditDistance, variants);

            foreach (var variant in variants)
                pairs.Add(new Posting(Hash(variant), wordIndex));
        }

        var array = pairs.ToArray();
        pairs.Clear();

        // Sorted by hash, then by word index so a variant's postings come out in a stable order.
        Array.Sort(array, static (a, b) =>
        {
            var byHash = a.Hash.CompareTo(b.Hash);
            return byHash != 0 ? byHash : a.WordIndex.CompareTo(b.WordIndex);
        });

        var distinct = 0;
        for (var i = 0; i < array.Length; i++)
            if (i == 0 || array[i].Hash != array[i - 1].Hash)
                distinct++;

        var hashes = new long[distinct];
        var starts = new int[distinct + 1];
        var postings = new int[array.Length];

        var slot = -1;
        for (var i = 0; i < array.Length; i++)
        {
            if (i == 0 || array[i].Hash != array[i - 1].Hash)
            {
                slot++;
                hashes[slot] = array[i].Hash;
                starts[slot] = i;
            }

            postings[i] = array[i].WordIndex;
        }

        starts[distinct] = array.Length;

        return new SymSpellIndex(dictionary, maxEditDistance, words, hashes, starts, postings);
    }

    /// <summary>Finds dictionary words within the configured max edit distance of <paramref name="input"/>, ranked closest-and-most-frequent first.</summary>
    public IReadOnlyList<Suggestion> Lookup(string input, int maxResults)
    {
        if (string.IsNullOrEmpty(input) || maxResults <= 0)
            return Array.Empty<Suggestion>();

        input = input.ToLowerInvariant();

        var variants = new HashSet<string>(StringComparer.Ordinal);
        CollectDeletes(input, _maxEditDistance, variants);

        // Word indices rather than strings: comparing and hashing ints costs nothing next to strings, and
        // this set is rebuilt on every keystroke that triggers a correction.
        var candidates = new HashSet<int>();

        foreach (var variant in variants)
        {
            var slot = FindSlot(Hash(variant));
            if (slot < 0) continue;

            for (var i = _postingStart[slot]; i < _postingStart[slot + 1]; i++)
                candidates.Add(_postings[i]);
        }

        var results = new List<Suggestion>(candidates.Count + 1);

        foreach (var index in candidates)
        {
            var candidate = _words[index];

            // The verification that makes hashed keys sound: a collision reaches here and leaves again.
            var distance = DamerauLevenshtein.Distance(input, candidate, _maxEditDistance);
            if (distance <= _maxEditDistance)
                results.Add(new Suggestion(candidate, _dictionary.GetFrequency(candidate), distance));
        }

        // The word itself may not be reachable through its own deletes if it was never indexed; adding it
        // here keeps "already correct" answers exact rather than depending on the index.
        if (_dictionary.Contains(input) && !results.Any(r => string.Equals(r.Word, input, StringComparison.Ordinal)))
            results.Add(new Suggestion(input, _dictionary.GetFrequency(input), 0));

        results.Sort(static (a, b) =>
        {
            var byDistance = a.EditDistance.CompareTo(b.EditDistance);
            return byDistance != 0 ? byDistance : b.Frequency.CompareTo(a.Frequency);
        });

        return results.Count <= maxResults ? results : results.GetRange(0, maxResults);
    }

    /// <summary>Index of <paramref name="hash"/> in <see cref="_variantHashes"/>, or -1.</summary>
    private int FindSlot(long hash)
    {
        var low = 0;
        var high = _variantHashes.Length - 1;

        while (low <= high)
        {
            var mid = (int)(((uint)low + (uint)high) >> 1);
            var value = _variantHashes[mid];

            if (value == hash) return mid;
            if (value < hash) low = mid + 1;
            else high = mid - 1;
        }

        return -1;
    }

    /// <summary>
    /// FNV-1a, 64-bit. Chosen for being deterministic and dependency-free rather than for speed: the
    /// framework's string hash is randomised per process, which would be fine within a run and impossible to
    /// persist, and a persisted index is the obvious next step from here.
    /// </summary>
    private static long Hash(string value)
    {
        unchecked
        {
            var hash = 14695981039346656037UL;
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }

            return (long)hash;
        }
    }

    /// <summary>
    /// Adds every string reachable from <paramref name="word"/> by deleting 0..maxDistance characters
    /// (0 deletions = the word itself) to <paramref name="into"/>.
    ///
    /// <para>Fills a caller-supplied set rather than returning a new one so the build can reuse a single set
    /// across 60,000 words instead of allocating one per word.</para>
    /// </summary>
    private static void CollectDeletes(string word, int maxDistance, HashSet<string> into)
    {
        into.Add(word);

        var currentLevel = new List<string> { word };

        for (var depth = 0; depth < maxDistance; depth++)
        {
            var nextLevel = new List<string>();

            foreach (var candidate in currentLevel)
            {
                if (candidate.Length == 0) continue;

                for (var i = 0; i < candidate.Length; i++)
                {
                    var deleted = candidate.Remove(i, 1);
                    if (into.Add(deleted)) nextLevel.Add(deleted);
                }
            }

            if (nextLevel.Count == 0) break;
            currentLevel = nextLevel;
        }
    }

    /// <summary>A (variant, word) pair during construction. A struct so the build array holds no references.</summary>
    private readonly struct Posting
    {
        public Posting(long hash, int wordIndex)
        {
            Hash = hash;
            WordIndex = wordIndex;
        }

        public long Hash { get; }
        public int WordIndex { get; }
    }
}
