namespace WordStrip.Core.Prediction;

/// <summary>
/// Offers an emoji for the word being typed, the way a phone keyboard does: type "pizza", get 🍕 on the bar
/// next to the words.
///
/// <para><b>A curated table, not a full Unicode dump.</b> There are several thousand emoji and tens of
/// thousands of CLDR keywords; shipping all of them would mean "a" matching a dozen faces and the bar
/// turning into a pictogram browser. This is a few hundred entries covering what people actually reach for
/// while writing, keyed on the words they would actually type.</para>
///
/// <para><b>At most one, and only on a confident match.</b> The bar has three to seven slots and they belong
/// to words. An emoji earns a slot by matching the whole word or completing it unambiguously — never by
/// fuzzy match, because a wrong emoji is far more jarring than a wrong word.</para>
/// </summary>
public sealed class EmojiSuggester
{
    /// <summary>
    /// Shortest prefix that can pull in an emoji. Two letters would put a face on the bar constantly; at
    /// three, matching is deliberate enough to feel like an answer rather than an interruption.
    /// </summary>
    public const int MinPrefixLength = 3;

    private readonly Dictionary<string, string> _exact = new(StringComparer.Ordinal);
    private readonly List<KeyValuePair<string, string>> _sorted;

    public static EmojiSuggester Default { get; } = new(EmojiTable.Entries);

    public EmojiSuggester(IEnumerable<(string Keyword, string Emoji)> entries)
    {
        foreach (var (keyword, emoji) in entries)
        {
            var key = keyword.Trim().ToLowerInvariant();
            if (key.Length == 0 || emoji.Length == 0) continue;

            // First definition of a keyword wins, so the table's ordering is the tie-break and the result is
            // never dependent on dictionary iteration order.
            _exact.TryAdd(key, emoji);
        }

        _sorted = _exact.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
    }

    public int Count => _exact.Count;

    /// <summary>
    /// The emoji for a word, or null.
    ///
    /// <para>An exact keyword match wins. Failing that, a prefix match is accepted only when exactly one
    /// emoji fits — "piz" gives 🍕 because nothing else begins that way, while "cal" stays silent because it
    /// opens both "calendar" and "call", and guessing between them would be worse than offering nothing.</para>
    /// </summary>
    public string? Match(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return null;

        var key = word.Trim().ToLowerInvariant();
        if (_exact.TryGetValue(key, out var exact)) return exact;

        if (key.Length < MinPrefixLength) return null;

        string? onlyMatch = null;

        // The table is sorted, so the matches form one contiguous run: find where it starts and stop at the
        // second entry. No scan of the whole table, and no allocation.
        var start = LowerBound(key);
        for (var i = start; i < _sorted.Count; i++)
        {
            if (!_sorted[i].Key.StartsWith(key, StringComparison.Ordinal)) break;

            if (onlyMatch is not null && !string.Equals(onlyMatch, _sorted[i].Value, StringComparison.Ordinal))
                return null;   // ambiguous: two different emoji fit, so offer neither

            onlyMatch ??= _sorted[i].Value;
        }

        return onlyMatch;
    }

    private int LowerBound(string prefix)
    {
        var low = 0;
        var high = _sorted.Count;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (string.CompareOrdinal(_sorted[mid].Key, prefix) < 0) low = mid + 1;
            else high = mid;
        }

        return low;
    }
}
