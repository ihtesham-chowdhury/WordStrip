namespace WordStrip.Core.Prediction;

/// <summary>
/// Loads a word→frequency table from a SymSpell-format dictionary file (one "word frequency" pair per line)
/// and exposes fast lookups against it. Fully offline — no network calls.
/// </summary>
public sealed class FrequencyDictionary
{
    private readonly Dictionary<string, long> _wordFrequency;

    public IReadOnlyDictionary<string, long> WordFrequency => _wordFrequency;

    private FrequencyDictionary(Dictionary<string, long> wordFrequency)
    {
        _wordFrequency = wordFrequency;
    }

    /// <param name="path">Path to a "word frequency" per-line dictionary file.</param>
    /// <param name="maxVocabularySize">
    /// Caps the loaded vocabulary to the N most frequent words. Keeps index build time and memory
    /// bounded — the long tail of a full dictionary is mostly rare/proper nouns that add little value
    /// for everyday typing and would otherwise slow down fuzzy-index construction significantly.
    /// </param>
    public static FrequencyDictionary LoadFromFile(string path, int maxVocabularySize = 60_000)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Frequency dictionary not found at '{path}'.", path);

        using var reader = new StreamReader(path);
        return LoadFromReader(reader, maxVocabularySize);
    }

    /// <summary>Loads from any source of "word frequency" lines — used for the dictionary embedded in the assembly.</summary>
    public static FrequencyDictionary LoadFromStream(Stream stream, int maxVocabularySize = 60_000)
    {
        using var reader = new StreamReader(stream);
        return LoadFromReader(reader, maxVocabularySize);
    }

    private static FrequencyDictionary LoadFromReader(TextReader reader, int maxVocabularySize)
    {
        var entries = new List<(string Word, long Frequency)>();

        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.TrimStart('\uFEFF').Trim();
            if (line.Length == 0) continue;

            var spaceIndex = line.IndexOf(' ');
            if (spaceIndex <= 0 || spaceIndex == line.Length - 1) continue;

            var word = line[..spaceIndex];
            var freqText = line[(spaceIndex + 1)..];
            if (!long.TryParse(freqText, out var freq)) continue;
            if (word.Length == 0) continue;

            entries.Add((word.ToLowerInvariant(), freq));
        }

        var map = new Dictionary<string, long>(Math.Min(entries.Count, maxVocabularySize), StringComparer.Ordinal);
        foreach (var (word, freq) in entries.OrderByDescending(e => e.Frequency).Take(maxVocabularySize))
        {
            map[word] = freq;
        }

        return new FrequencyDictionary(map);
    }

    public bool Contains(string word) => _wordFrequency.ContainsKey(word);

    public long GetFrequency(string word) => _wordFrequency.TryGetValue(word, out var freq) ? freq : 0;
}
