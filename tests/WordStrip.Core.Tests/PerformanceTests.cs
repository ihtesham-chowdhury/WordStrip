using System.Diagnostics;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// Timings against the real 60,000-word vocabulary, not the toy test list.
///
/// <para>The assertions are deliberately loose — these run on shared CI-style hardware and are meant to
/// catch an order-of-magnitude regression, not to police microseconds. The measured numbers are written to
/// the test output, which is where the actual value is.</para>
/// </summary>
public sealed class PerformanceTests
{
    private const int MaxVocabulary = 60_000;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PerformanceTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string? FindDictionary()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "assets", "dict", "frequency_dictionary_en_82_765.txt");
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    [Fact]
    public void MeasureRealVocabularyPerformance()
    {
        var path = FindDictionary();
        if (path is null)
        {
            // The dictionary lives in the repo, not the test output; skip rather than fail if it moved.
            _output.WriteLine("Real dictionary not found next to the test binary — skipping timings.");
            return;
        }

        var loadWatch = Stopwatch.StartNew();
        var dictionary = FrequencyDictionary.LoadFromFile(path, MaxVocabulary);
        loadWatch.Stop();

        var prefixWatch = Stopwatch.StartNew();
        var prefixIndex = PrefixIndex.Build(dictionary);
        prefixWatch.Stop();

        var symSpellWatch = Stopwatch.StartNew();
        var symSpell = SymSpellIndex.Build(dictionary, maxEditDistance: 2);
        symSpellWatch.Stop();

        var engine = new PredictionEngine(dictionary, symSpell, prefixIndex);

        _output.WriteLine($"vocabulary          : {dictionary.WordFrequency.Count:N0} words");
        _output.WriteLine($"dictionary load     : {loadWatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"prefix index build  : {prefixWatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"symspell index build: {symSpellWatch.ElapsedMilliseconds} ms");

        _output.WriteLine($"live suggestions    : {MeasureMicroseconds(() => engine.GetLiveSuggestions("wor", 5)):F1} µs/call");
        _output.WriteLine($"  single letter     : {MeasureMicroseconds(() => engine.GetLiveSuggestions("a", 5)):F1} µs/call");
        _output.WriteLine($"  long prefix       : {MeasureMicroseconds(() => engine.GetLiveSuggestions("intern", 5)):F1} µs/call");
        _output.WriteLine($"fuzzy lookup        : {MeasureMicroseconds(() => symSpell.Lookup("recieve", 5)):F1} µs/call");
        _output.WriteLine($"autocorrection      : {MeasureMicroseconds(() => engine.GetAutocorrection("recieve")):F1} µs/call");
        _output.WriteLine($"frequent words      : {MeasureMicroseconds(() => engine.GetFrequentWords(5)):F1} µs/call");

        // Before/after for the change that replaced a full-vocabulary scan with a binary-searched range.
        // The old implementation is reproduced here rather than kept in the product, purely to measure it.
        _output.WriteLine("");
        _output.WriteLine("prefix lookup, old full-scan vs new indexed:");
        foreach (var prefix in new[] { "a", "wor", "intern" })
        {
            var scan = MeasureMicroseconds(() => LegacyPrefixScan(dictionary, prefix, 5));
            var indexed = MeasureMicroseconds(() => prefixIndex.FindByPrefix(prefix, 64));
            _output.WriteLine($"  \"{prefix,-6}\" scan {scan,8:F1} µs   indexed {indexed,7:F1} µs   {scan / Math.Max(indexed, 0.001),5:F1}x faster");
        }

        // A keystroke budget of one millisecond leaves the UI thread entirely free at any realistic typing
        // speed; the real figures are far below this.
        var liveMicroseconds = MeasureMicroseconds(() => engine.GetLiveSuggestions("wor", 5));
        Assert.True(liveMicroseconds < 1000,
            $"Live suggestions took {liveMicroseconds:F1} µs/call, which is too slow for per-keystroke use.");

        // The persistent bar asks for this every time it reappears between words.
        var frequentMicroseconds = MeasureMicroseconds(() => engine.GetFrequentWords(5));
        Assert.True(frequentMicroseconds < 1000,
            $"Frequent-word lookup took {frequentMicroseconds:F1} µs/call; it must be served from cache.");
    }

    [Fact]
    public void MeasureLanguageModelPerformance()
    {
        var dictionaryPath = FindDictionary();
        var modelDirectory = FindNGramDirectory();

        if (dictionaryPath is null || modelDirectory is null)
        {
            _output.WriteLine("Real dictionary or n-gram model not found next to the test binary — skipping timings.");
            return;
        }

        var dictionary = FrequencyDictionary.LoadFromFile(dictionaryPath, MaxVocabulary);

        // Measured against a settled baseline: the dictionary is already loaded, so this is the model's own
        // cost and not the cost of everything that happens to precede it at startup.
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var loadWatch = Stopwatch.StartNew();
        var model = NGramLanguageModel.Load(modelDirectory, embeddedResourceAssembly: null, dictionary);
        loadWatch.Stop();
        var after = GC.GetTotalMemory(forceFullCollection: true);

        var bigramFile = new FileInfo(Path.Combine(modelDirectory, NGramFormat.FileName(2)));
        var trigramFile = new FileInfo(Path.Combine(modelDirectory, NGramFormat.FileName(3)));

        _output.WriteLine($"model files         : {(bigramFile.Length + trigramFile.Length) / 1024.0 / 1024.0:F2} MB on disk");
        _output.WriteLine($"  bigram            : {bigramFile.Length / 1024.0 / 1024.0:F2} MB, {model.BigramContextCount:N0} contexts");
        _output.WriteLine($"  trigram           : {trigramFile.Length / 1024.0 / 1024.0:F2} MB, {model.TrigramContextCount:N0} contexts");
        _output.WriteLine($"model load          : {loadWatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"model resident      : {(after - before) / 1024.0 / 1024.0:F1} MB");

        var engine = new PredictionEngine(
            dictionary, SymSpellIndex.Build(dictionary, maxEditDistance: 2), languageModel: model);

        var trigramContext = PredictionContext.After("i", "am");
        var bigramContext = PredictionContext.After("qwerty", "looking");
        var unseenContext = PredictionContext.After("qwerty", "asdfgh");

        _output.WriteLine("");
        _output.WriteLine($"next word, trigram  : {MeasureMicroseconds(() => model.GetNextWordCandidates(trigramContext, 5)):F1} µs/call");
        _output.WriteLine($"next word, bigram   : {MeasureMicroseconds(() => model.GetNextWordCandidates(bigramContext, 5)):F1} µs/call");
        _output.WriteLine($"next word, unseen   : {MeasureMicroseconds(() => model.GetNextWordCandidates(unseenContext, 5)):F1} µs/call");
        _output.WriteLine($"single word score   : {MeasureMicroseconds(() => model.GetLogScore(trigramContext, "looking")):F1} µs/call");

        _output.WriteLine("");
        _output.WriteLine($"end to end, next word    : {MeasureMicroseconds(() => engine.GetNextWords(trigramContext, 5)):F1} µs/call");
        _output.WriteLine($"end to end, completion   : {MeasureMicroseconds(() => engine.GetLiveSuggestions("wor", 5, trigramContext)):F1} µs/call");
        _output.WriteLine($"  without context        : {MeasureMicroseconds(() => engine.GetLiveSuggestions("wor", 5)):F1} µs/call");

        _output.WriteLine("");
        _output.WriteLine("sample predictions:");
        foreach (var (first, second) in new[] { ("i", "am"), ("thank", "you"), ("how", "are"), ("let", "me"), ("it", "is") })
        {
            var words = engine.GetNextWords(PredictionContext.After(first, second), 4).Select(s => s.Word);
            _output.WriteLine($"  \"{first} {second}\" -> {string.Join(", ", words)}");
        }

        // Both prediction paths run on every keystroke, so both live inside the same budget the completion
        // path has always had.
        var nextWordMicroseconds = MeasureMicroseconds(() => engine.GetNextWords(trigramContext, 5));
        Assert.True(nextWordMicroseconds < 1000,
            $"Next-word prediction took {nextWordMicroseconds:F1} µs/call, which is too slow for per-keystroke use.");

        var contextualMicroseconds = MeasureMicroseconds(() => engine.GetLiveSuggestions("wor", 5, trigramContext));
        Assert.True(contextualMicroseconds < 1000,
            $"Contextual completion took {contextualMicroseconds:F1} µs/call, which is too slow for per-keystroke use.");
    }

    private static string? FindNGramDirectory()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "assets", "ngram");
            if (File.Exists(Path.Combine(candidate, NGramFormat.FileName(2)))) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>The pre-Phase-1 prefix lookup, kept only as a performance baseline.</summary>
    private static List<Suggestion> LegacyPrefixScan(FrequencyDictionary dictionary, string prefix, int maxResults) =>
        dictionary.WordFrequency
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => new Suggestion(kv.Key, kv.Value, kv.Key == prefix ? 0 : 1))
            .OrderByDescending(s => s.Frequency)
            .Take(maxResults)
            .ToList();

    private static double MeasureMicroseconds(Action action, int iterations = 200)
    {
        for (var i = 0; i < 20; i++) action(); // let the JIT settle before timing

        var watch = Stopwatch.StartNew();
        for (var i = 0; i < iterations; i++) action();
        watch.Stop();

        return watch.Elapsed.TotalMilliseconds * 1000 / iterations;
    }
}
