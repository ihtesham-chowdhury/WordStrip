using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// Measures what each part of the prediction stack actually costs in managed memory.
///
/// <para><b>Why this exists.</b> "The SymSpell edit-distance-2 index is the bulk of the memory" has sat in
/// this project's own documentation as an unverified assumption for months, and an outside reviewer
/// independently proposed fixes for it — a Bloom filter, a DAWG, an FST. All are real techniques and all are
/// worthless if the assumption is wrong. Optimising an unmeasured hypothesis is how weeks disappear. This
/// measures each component in isolation so that whatever is done next is aimed at something.</para>
///
/// <para><b>What it does not measure.</b> Managed heap only. The running application's working set also
/// holds the CLR, WPF's rendering tree, font caches, the ONNX runtime and its native arena, and every Win32
/// handle in the process — none of which appear here. This is a floor for the prediction stack, not an
/// account of the whole process, and the gap between the two is itself the finding.</para>
/// </summary>
public sealed class MemoryProfileTests
{
    private const int MaxVocabulary = 60_000;

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public MemoryProfileTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    private static string? FindDictionary() => FindUpwards(Path.Combine("assets", "dict", "frequency_dictionary_en_82_765.txt"), file: true);

    private static string? FindNGramDirectory() => FindUpwards(Path.Combine("assets", "ngram"), file: false);

    private static string? FindUpwards(string relative, bool file)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, relative);
            if (file ? File.Exists(candidate) : Directory.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    /// <summary>
    /// Settles the heap and reports its size. Repeated because the first collection can push finalizable
    /// objects onto the finalizer queue, and their memory is only reclaimed by a later one.
    /// </summary>
    private static long SettledBytes()
    {
        for (var i = 0; i < 3; i++)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        return GC.GetTotalMemory(forceFullCollection: true);
    }

    private static string Mb(long bytes) => $"{bytes / 1024.0 / 1024.0,8:F1} MB";

    [Fact]
    public void MeasureComponentMemory()
    {
        var dictionaryPath = FindDictionary();
        if (dictionaryPath is null)
        {
            _output.WriteLine("Shipped dictionary not found — skipping.");
            return;
        }

        _output.WriteLine("Managed heap only. Excludes the CLR, WPF, ONNX and all native allocation.");
        _output.WriteLine("");

        var start = SettledBytes();

        var dictionary = FrequencyDictionary.LoadFromFile(dictionaryPath, MaxVocabulary);
        var afterDictionary = SettledBytes();
        _output.WriteLine($"dictionary ({dictionary.WordFrequency.Count:N0} words)  {Mb(afterDictionary - start)}");

        var prefixIndex = PrefixIndex.Build(dictionary);
        var afterPrefix = SettledBytes();
        _output.WriteLine($"prefix index             {Mb(afterPrefix - afterDictionary)}");

        var symSpell = SymSpellIndex.Build(dictionary, maxEditDistance: 2);
        var afterSymSpell = SettledBytes();
        _output.WriteLine($"SymSpell (edit dist 2)   {Mb(afterSymSpell - afterPrefix)}   <-- the assumption");

        NGramLanguageModel? model = null;
        var modelDirectory = FindNGramDirectory();
        if (modelDirectory is not null)
        {
            model = NGramLanguageModel.Load(modelDirectory, embeddedResourceAssembly: null, dictionary);
            _output.WriteLine($"n-gram model             {Mb(SettledBytes() - afterSymSpell)}");
        }

        _output.WriteLine("");
        _output.WriteLine($"prediction stack total   {Mb(SettledBytes() - start)}");
        _output.WriteLine("");
        _output.WriteLine("Whatever the running app uses beyond this is CLR, WPF, ONNX or native.");

        // Keep everything reachable, or the collector may reclaim what was just measured.
        GC.KeepAlive(dictionary);
        GC.KeepAlive(prefixIndex);
        GC.KeepAlive(symSpell);
        GC.KeepAlive(model);
    }

    /// <summary>
    /// How the fuzzy index scales with edit distance. Worth knowing on its own: it is one constant in the
    /// code, and it is the difference between a correction for "recieve" and one for "recieev".
    /// </summary>
    [Fact]
    public void MeasureSymSpellByEditDistance()
    {
        var dictionaryPath = FindDictionary();
        if (dictionaryPath is null)
        {
            _output.WriteLine("Shipped dictionary not found — skipping.");
            return;
        }

        var dictionary = FrequencyDictionary.LoadFromFile(dictionaryPath, MaxVocabulary);

        foreach (var distance in new[] { 1, 2 })
        {
            var before = SettledBytes();
            var index = SymSpellIndex.Build(dictionary, maxEditDistance: distance);
            var after = SettledBytes();

            _output.WriteLine($"edit distance {distance}          {Mb(after - before)}");

            GC.KeepAlive(index);
        }

        GC.KeepAlive(dictionary);
    }
}
