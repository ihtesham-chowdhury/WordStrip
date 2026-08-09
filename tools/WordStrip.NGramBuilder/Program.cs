using System.Diagnostics;
using System.Globalization;
using System.Text;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.NGramBuilder;

/// <summary>
/// Builds <c>assets\ngram\ngram-2.txt</c> and <c>ngram-3.txt</c> from the corpus fetched by
/// <c>tools\ngram\Fetch-Corpus.ps1</c>.
///
/// <para>Run: <c>dotnet run --project tools\WordStrip.NGramBuilder -c Release</c></para>
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var options = BuilderOptions.Parse(args);
        var stopwatch = Stopwatch.StartNew();

        Console.WriteLine("WordStrip n-gram model builder");
        Console.WriteLine($"  corpus:     {options.CorpusDirectory}");
        Console.WriteLine($"  output:     {options.OutputDirectory}");
        Console.WriteLine($"  vocabulary: top {options.MaxVocabularySize:N0} words");
        Console.WriteLine($"  pruning:    min bigram {options.MinBigramCount}, min trigram {options.MinTrigramCount}, top {options.TopContinuations} per context");
        Console.WriteLine();

        if (!Directory.Exists(options.CorpusDirectory))
        {
            Console.Error.WriteLine($"Corpus directory not found: {options.CorpusDirectory}");
            Console.Error.WriteLine("Run tools\\ngram\\Fetch-Corpus.ps1 first.");
            return 1;
        }

        // The model may only predict words the rest of the app can actually offer. Restricting to the same
        // 60k vocabulary the completion engine loads also throws out the bulk of what makes a raw literary
        // corpus unusable here: proper nouns, OCR debris, archaic spellings and foreign words.
        Console.WriteLine("Loading vocabulary...");
        var vocabulary = FrequencyDictionary.LoadFromFile(options.DictionaryPath, options.MaxVocabularySize);
        Console.WriteLine($"  {vocabulary.WordFrequency.Count:N0} words");

        var counts = CorpusCounter.Count(options, vocabulary);

        Console.WriteLine();
        Console.WriteLine("Blending sources...");
        var symSpell = SymSpellBigrams.Load(options.SymSpellBigramPath, vocabulary);
        Console.WriteLine($"  SymSpell bigrams usable after vocabulary filtering: {symSpell.TotalPairs:N0}");

        var bigrams = ModelBlender.BuildBigramModel(counts, symSpell, options);
        var trigrams = ModelBlender.BuildTrigramModel(counts, options);

        Directory.CreateDirectory(options.OutputDirectory);
        var bigramPath = Path.Combine(options.OutputDirectory, NGramFormat.FileName(2));
        var trigramPath = Path.Combine(options.OutputDirectory, NGramFormat.FileName(3));

        ModelWriter.Write(bigramPath, order: 2, bigrams, options,
            "Project Gutenberg (public domain) blended with SymSpell bigrams (MIT)");
        ModelWriter.Write(trigramPath, order: 3, trigrams, options,
            "Project Gutenberg (public domain)");

        stopwatch.Stop();

        Console.WriteLine();
        Console.WriteLine("Done in {0:N1}s", stopwatch.Elapsed.TotalSeconds);
        Report(bigramPath, "bigram");
        Report(trigramPath, "trigram");
        Console.WriteLine($"  peak working set: {Process.GetCurrentProcess().PeakWorkingSet64 / 1024 / 1024:N0} MB");
        return 0;
    }

    private static void Report(string path, string label)
    {
        var info = new FileInfo(path);
        Console.WriteLine($"  {label,-8} {info.Length / 1024.0 / 1024.0,6:N2} MB   {path}");
    }
}

/// <summary>Tunables. Every one of them changes the model, so they are recorded in the output header.</summary>
internal sealed class BuilderOptions
{
    public required string CorpusDirectory { get; init; }
    public required string OutputDirectory { get; init; }
    public required string DictionaryPath { get; init; }
    public required string SymSpellBigramPath { get; init; }

    public int MaxVocabularySize { get; init; } = 60_000;

    /// <summary>Minimum corpus occurrences before an n-gram is trusted. One or two sightings in a few dozen novels is noise.</summary>
    public int MinBigramCount { get; init; } = 4;
    public int MinTrigramCount { get; init; } = 5;

    /// <summary>
    /// How many continuations to keep per context. The bar shows at most 7, but a few more are kept so the
    /// ranker has room to drop candidates that fail other checks and still have something to show.
    /// </summary>
    public int TopContinuations { get; init; } = 10;

    /// <summary>
    /// Weight given to the SymSpell bigram distribution where both sources know a context. The two are
    /// mixed as probabilities rather than counts, so this is a genuine mixture weight and not a fudge
    /// factor. Even because neither source is clearly better: SymSpell is broader and more modern, Gutenberg
    /// is narrower but real running prose.
    /// </summary>
    public double SymSpellWeight { get; init; } = 0.5;

    public static BuilderOptions Parse(string[] args)
    {
        var repoRoot = FindRepositoryRoot();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                map[args[i][2..]] = args[i + 1];
        }

        int IntOption(string name, int fallback) =>
            map.TryGetValue(name, out var raw) && int.TryParse(raw, out var value) ? value : fallback;

        return new BuilderOptions
        {
            CorpusDirectory = map.GetValueOrDefault("corpus", Path.Combine(repoRoot, ".corpus", "gutenberg")),
            OutputDirectory = map.GetValueOrDefault("output", Path.Combine(repoRoot, "assets", "ngram")),
            DictionaryPath = map.GetValueOrDefault("dictionary",
                Path.Combine(repoRoot, "assets", "dict", "frequency_dictionary_en_82_765.txt")),
            SymSpellBigramPath = map.GetValueOrDefault("symspell",
                Path.Combine(repoRoot, ".corpus", "symspell_bigrams.txt")),
            MinBigramCount = IntOption("min-bigram", 4),
            MinTrigramCount = IntOption("min-trigram", 5),
            TopContinuations = IntOption("top", 10),
        };
    }

    /// <summary>Walks up from the executable until it finds the solution, so the tool works from any working directory.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "WordStrip.sln")))
            directory = directory.Parent;

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate WordStrip.sln above the build output.");
    }
}

/// <summary>Raw occurrence counts over the Gutenberg corpus.</summary>
internal sealed class CorpusCounts
{
    public Dictionary<string, int> Unigrams { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Bigrams { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Trigrams { get; } = new(StringComparer.Ordinal);
    public long TokenCount { get; set; }

    /// <summary>
    /// Composite keys are joined with a unit separator rather than held as tuples. A
    /// <c>Dictionary&lt;(string,string,string),int&gt;</c> over several million distinct trigrams spends far
    /// more on tuple hashing and boxing than one flat string costs, and the separator cannot collide with a
    /// token because tokens are letters and apostrophes only.
    /// </summary>
    public const char KeySeparator = '\u0001';
    private const string KeySeparatorText = "\u0001";

    public static string Key(string a, string b) => string.Concat(a, KeySeparatorText, b);
    public static string Key(string a, string b, string c) =>
        string.Concat(a, KeySeparatorText, b, KeySeparatorText, c);
}

internal static class CorpusCounter
{
    public static CorpusCounts Count(BuilderOptions options, FrequencyDictionary vocabulary)
    {
        var counts = new CorpusCounts();
        var files = Directory.GetFiles(options.CorpusDirectory, "*.txt").OrderBy(f => f, StringComparer.Ordinal).ToList();

        Console.WriteLine();
        Console.WriteLine($"Counting n-grams across {files.Count} files...");

        // Interning keeps one string instance per distinct token. Without it the same word allocates afresh
        // for every one of its millions of occurrences, and the composite keys hold references to all of them.
        var interned = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NGramFormat.SentenceStart] = NGramFormat.SentenceStart,
        };

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            string? previous = null;
            string? beforePrevious = null;

            foreach (var rawToken in NGramTokenizer.Tokenize(text))
            {
                var isBoundary = ReferenceEquals(rawToken, NGramFormat.SentenceStart) || rawToken == NGramFormat.SentenceStart;

                // Out-of-vocabulary words break the chain rather than being skipped over. Stepping past them
                // would invent adjacencies that never occurred — "the <name> said" must not teach the model
                // that "the" is followed by "said".
                if (!isBoundary && !vocabulary.Contains(rawToken))
                {
                    previous = null;
                    beforePrevious = null;
                    continue;
                }

                if (!interned.TryGetValue(rawToken, out var token))
                {
                    token = rawToken;
                    interned[token] = token;
                }

                counts.TokenCount++;
                Increment(counts.Unigrams, token);

                if (previous is not null)
                {
                    Increment(counts.Bigrams, CorpusCounts.Key(previous, token));

                    if (beforePrevious is not null)
                        Increment(counts.Trigrams, CorpusCounts.Key(beforePrevious, previous, token));
                }

                beforePrevious = previous;
                previous = token;
            }
        }

        Console.WriteLine($"  {counts.TokenCount:N0} tokens");
        Console.WriteLine($"  {counts.Unigrams.Count:N0} distinct unigrams");
        Console.WriteLine($"  {counts.Bigrams.Count:N0} distinct bigrams");
        Console.WriteLine($"  {counts.Trigrams.Count:N0} distinct trigrams");
        return counts;
    }

    private static void Increment(Dictionary<string, int> table, string key) =>
        table[key] = table.TryGetValue(key, out var existing) ? existing + 1 : 1;
}

/// <summary>SymSpell's bigram counts, reduced to a conditional distribution per context word.</summary>
internal sealed class SymSpellBigrams
{
    private readonly Dictionary<string, Dictionary<string, long>> _byContext = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _contextTotals = new(StringComparer.Ordinal);

    public long TotalPairs { get; private set; }

    public bool TryGetProbability(string context, string next, out double probability)
    {
        probability = 0;
        if (!_byContext.TryGetValue(context, out var continuations)) return false;
        if (!continuations.TryGetValue(next, out var count)) return false;

        probability = (double)count / _contextTotals[context];
        return true;
    }

    public IEnumerable<KeyValuePair<string, Dictionary<string, long>>> Contexts => _byContext;

    public bool TryGetContinuations(string context, out Dictionary<string, long> continuations) =>
        _byContext.TryGetValue(context, out continuations!);

    public static SymSpellBigrams Load(string path, FrequencyDictionary vocabulary)
    {
        var model = new SymSpellBigrams();
        if (!File.Exists(path))
        {
            Console.WriteLine($"  (no SymSpell bigram file at {path} — using Gutenberg alone)");
            return model;
        }

        foreach (var line in File.ReadLines(path))
        {
            // "word1 word2 count"
            var first = line.IndexOf(' ');
            if (first <= 0) continue;
            var second = line.IndexOf(' ', first + 1);
            if (second <= first) continue;

            var w1 = NGramTokenizer.Normalize(line[..first]);
            var w2 = NGramTokenizer.Normalize(line[(first + 1)..second]);
            if (w1.Length == 0 || w2.Length == 0) continue;
            if (!vocabulary.Contains(w1) || !vocabulary.Contains(w2)) continue;
            if (!long.TryParse(line[(second + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) continue;
            if (count <= 0) continue;

            if (!model._byContext.TryGetValue(w1, out var continuations))
            {
                continuations = new Dictionary<string, long>(StringComparer.Ordinal);
                model._byContext[w1] = continuations;
            }

            continuations[w2] = continuations.TryGetValue(w2, out var existing) ? existing + count : count;
            model._contextTotals[w1] = model._contextTotals.GetValueOrDefault(w1) + count;
            model.TotalPairs++;
        }

        return model;
    }
}

/// <summary>One line of the output: a context, a predicted word, and the log10 of its blended probability.</summary>
internal readonly record struct ModelEntry(string Context, string Next, double LogProbability);

internal static class ModelBlender
{
    public static List<ModelEntry> BuildBigramModel(CorpusCounts counts, SymSpellBigrams symSpell, BuilderOptions options)
    {
        // Group the surviving corpus bigrams by their context word.
        var corpusByContext = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (key, count) in counts.Bigrams)
        {
            if (count < options.MinBigramCount) continue;

            var split = key.IndexOf(CorpusCounts.KeySeparator);
            var context = key[..split];
            var next = key[(split + 1)..];

            if (!corpusByContext.TryGetValue(context, out var continuations))
            {
                continuations = new Dictionary<string, int>(StringComparer.Ordinal);
                corpusByContext[context] = continuations;
            }
            continuations[next] = count;
        }

        var contexts = new HashSet<string>(corpusByContext.Keys, StringComparer.Ordinal);
        foreach (var (context, _) in symSpell.Contexts) contexts.Add(context);

        var entries = new List<ModelEntry>();

        foreach (var context in contexts)
        {
            var hasCorpus = corpusByContext.TryGetValue(context, out var corpusContinuations);
            var corpusTotal = hasCorpus ? corpusContinuations!.Values.Sum(v => (long)v) : 0L;

            var candidates = new HashSet<string>(StringComparer.Ordinal);
            if (hasCorpus) foreach (var word in corpusContinuations!.Keys) candidates.Add(word);
            if (symSpell.TryGetContinuations(context, out var symContinuations))
                foreach (var word in symContinuations.Keys) candidates.Add(word);

            var blended = new List<ModelEntry>(candidates.Count);

            foreach (var next in candidates)
            {
                var corpusP = hasCorpus && corpusContinuations!.TryGetValue(next, out var c) && corpusTotal > 0
                    ? (double)c / corpusTotal
                    : (double?)null;

                var symP = symSpell.TryGetProbability(context, next, out var s) ? s : (double?)null;

                // Where only one source knows a context, that source is the whole distribution rather than
                // half of it. Mixing against an implicit zero would halve every probability in contexts the
                // other source simply never saw, which is not evidence against them.
                var probability = (corpusP, symP) switch
                {
                    ({ } cp, { } sp) => (1 - options.SymSpellWeight) * cp + options.SymSpellWeight * sp,
                    ({ } cp, null) => cp,
                    (null, { } sp) => sp,
                    _ => 0,
                };

                if (probability > 0)
                    blended.Add(new ModelEntry(context, next, Math.Log10(probability)));
            }

            AddTopN(entries, blended, options.TopContinuations);
        }

        Console.WriteLine($"  bigram entries after blending and pruning: {entries.Count:N0}");
        return entries;
    }

    public static List<ModelEntry> BuildTrigramModel(CorpusCounts counts, BuilderOptions options)
    {
        var byContext = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var (key, count) in counts.Trigrams)
        {
            if (count < options.MinTrigramCount) continue;

            var lastSeparator = key.LastIndexOf(CorpusCounts.KeySeparator);

            // The two context words are emitted as separate tab-delimited fields, so the internal key
            // separator has to become the field separator here rather than leaking into the output file.
            var context = key[..lastSeparator].Replace(CorpusCounts.KeySeparator, NGramFormat.FieldSeparator);
            var next = key[(lastSeparator + 1)..];

            if (!byContext.TryGetValue(context, out var continuations))
            {
                continuations = new Dictionary<string, int>(StringComparer.Ordinal);
                byContext[context] = continuations;
            }
            continuations[next] = count;
        }

        var entries = new List<ModelEntry>();

        foreach (var (context, continuations) in byContext)
        {
            // Conditioned on the context pair's own total, not on how often the trigram occurs overall:
            // "P(next | these two words)" is the question, so the denominator is every time those two words
            // appeared together with a surviving continuation.
            var total = continuations.Values.Sum(v => (long)v);
            if (total <= 0) continue;

            var scored = continuations
                .Select(kv => new ModelEntry(context, kv.Key, Math.Log10((double)kv.Value / total)))
                .ToList();

            AddTopN(entries, scored, options.TopContinuations);
        }

        Console.WriteLine($"  trigram entries after pruning: {entries.Count:N0}");
        return entries;
    }

    /// <summary>Keeps the most probable continuations, breaking ties ordinally so the output is reproducible.</summary>
    private static void AddTopN(List<ModelEntry> destination, List<ModelEntry> candidates, int keep)
    {
        // The sentence marker is a legitimate context — that is how the model knows which words open a
        // sentence — but it is never a word anyone can type, so it must not occupy the predicted slot.
        // Dropped before the top-N cut so it cannot take a place from a real word: for "thank you" it was
        // otherwise the single most probable continuation, and the bar would have shown nothing there.
        //
        // Its probability mass is deliberately left in the denominator rather than redistributed. A context
        // that usually ends the sentence genuinely predicts its continuations less strongly, and the
        // remaining probabilities summing to less than one is the honest way to say so.
        candidates.RemoveAll(static e => string.Equals(e.Next, NGramFormat.SentenceStart, StringComparison.Ordinal));

        candidates.Sort(static (a, b) =>
        {
            var byProbability = b.LogProbability.CompareTo(a.LogProbability);
            return byProbability != 0 ? byProbability : string.CompareOrdinal(a.Next, b.Next);
        });

        for (var i = 0; i < Math.Min(keep, candidates.Count); i++)
            destination.Add(candidates[i]);
    }
}

internal static class ModelWriter
{
    public static void Write(string path, int order, List<ModelEntry> entries, BuilderOptions options, string sources)
    {
        // Sorted by context then by descending probability. Both the app and a human reading a diff benefit:
        // regenerating from the same corpus produces a byte-identical file, so a real change to the model
        // shows up as a real change in the diff.
        entries.Sort(static (a, b) =>
        {
            var byContext = string.CompareOrdinal(a.Context, b.Context);
            if (byContext != 0) return byContext;

            var byProbability = b.LogProbability.CompareTo(a.LogProbability);
            return byProbability != 0 ? byProbability : string.CompareOrdinal(a.Next, b.Next);
        });

        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        writer.WriteLine($"{NGramFormat.CommentPrefix} WordStrip n-gram model, order {order}");
        writer.WriteLine($"{NGramFormat.CommentPrefix} sources: {sources}");
        writer.WriteLine($"{NGramFormat.CommentPrefix} fields: {(order == 2 ? "context" : "context1<TAB>context2")}<TAB>next<TAB>log10(P(next|context))");
        writer.WriteLine($"{NGramFormat.CommentPrefix} vocabulary: top {options.MaxVocabularySize} words of the bundled frequency dictionary");
        writer.WriteLine($"{NGramFormat.CommentPrefix} pruning: min count {(order == 2 ? options.MinBigramCount : options.MinTrigramCount)}, top {options.TopContinuations} continuations per context");
        writer.WriteLine($"{NGramFormat.CommentPrefix} entries: {entries.Count}");
        writer.WriteLine($"{NGramFormat.CommentPrefix} regenerate: tools\\ngram\\Fetch-Corpus.ps1 then dotnet run --project tools\\WordStrip.NGramBuilder -c Release");

        foreach (var entry in entries)
        {
            writer.Write(entry.Context);           // already tab-separated for order 3
            writer.Write(NGramFormat.FieldSeparator);
            writer.Write(entry.Next);
            writer.Write(NGramFormat.FieldSeparator);
            writer.WriteLine(entry.LogProbability.ToString("0.####", CultureInfo.InvariantCulture));
        }
    }
}
