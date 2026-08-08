namespace WordStrip.Core.Prediction;

/// <summary>
/// SymSpell-style fuzzy lookup: precomputes every "delete" variant of every dictionary word (removing up
/// to <see cref="_maxEditDistance"/> characters) into an index, then finds fuzzy matches for an input word
/// by generating the same deletes for it and checking which dictionary words share a variant. This trades
/// index build time and memory for lookup speed — much faster than comparing the input against every
/// dictionary word directly, which matters since corrections need to happen live as the user types.
/// </summary>
public sealed class SymSpellIndex
{
    private readonly FrequencyDictionary _dictionary;
    private readonly int _maxEditDistance;
    private readonly Dictionary<string, List<string>> _deleteIndex;

    private SymSpellIndex(FrequencyDictionary dictionary, int maxEditDistance, Dictionary<string, List<string>> deleteIndex)
    {
        _dictionary = dictionary;
        _maxEditDistance = maxEditDistance;
        _deleteIndex = deleteIndex;
    }

    public static SymSpellIndex Build(FrequencyDictionary dictionary, int maxEditDistance = 2)
    {
        var deleteIndex = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var word in dictionary.WordFrequency.Keys)
        {
            foreach (var variant in GenerateDeletes(word, maxEditDistance))
            {
                if (!deleteIndex.TryGetValue(variant, out var list))
                {
                    list = new List<string>();
                    deleteIndex[variant] = list;
                }
                list.Add(word);
            }
        }

        return new SymSpellIndex(dictionary, maxEditDistance, deleteIndex);
    }

    /// <summary>Finds dictionary words within the configured max edit distance of <paramref name="input"/>, ranked closest-and-most-frequent first.</summary>
    public IReadOnlyList<Suggestion> Lookup(string input, int maxResults)
    {
        if (string.IsNullOrEmpty(input) || maxResults <= 0)
            return Array.Empty<Suggestion>();

        input = input.ToLowerInvariant();
        var candidates = new HashSet<string>(StringComparer.Ordinal);

        foreach (var variant in GenerateDeletes(input, _maxEditDistance))
        {
            if (_deleteIndex.TryGetValue(variant, out var words))
            {
                foreach (var w in words) candidates.Add(w);
            }
        }

        if (_dictionary.Contains(input)) candidates.Add(input);

        var results = new List<Suggestion>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var distance = DamerauLevenshtein.Distance(input, candidate, _maxEditDistance);
            if (distance <= _maxEditDistance)
            {
                results.Add(new Suggestion(candidate, _dictionary.GetFrequency(candidate), distance));
            }
        }

        results.Sort((a, b) =>
        {
            var byDistance = a.EditDistance.CompareTo(b.EditDistance);
            return byDistance != 0 ? byDistance : b.Frequency.CompareTo(a.Frequency);
        });

        return results.Count <= maxResults ? results : results.GetRange(0, maxResults);
    }

    /// <summary>All strings reachable from <paramref name="word"/> by deleting 0..maxDistance characters (0 deletions = the word itself).</summary>
    private static HashSet<string> GenerateDeletes(string word, int maxDistance)
    {
        var results = new HashSet<string>(StringComparer.Ordinal) { word };
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
                    if (results.Add(deleted))
                    {
                        nextLevel.Add(deleted);
                    }
                }
            }

            if (nextLevel.Count == 0) break;
            currentLevel = nextLevel;
        }

        return results;
    }
}
