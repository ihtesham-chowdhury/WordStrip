using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// A hand-written n-gram model shared by the Phase 2 tests.
///
/// <para>Deliberately tiny and written out in the model's own text format rather than generated from the
/// real corpus. Every probability here was chosen so the expected answer can be worked out by reading the
/// fixture, which is the only way an assertion about ranking is worth anything — against the shipped model
/// a test would be asserting what a few dozen novels happen to contain, and would change whenever the corpus
/// did.</para>
///
/// <para>The shape is built to exercise backoff specifically. "i am looking" has a trigram; "am looking"
/// deliberately has no bigram entry under "looking" for one of its words; "zebra" has no context at all and
/// must fall through to the unigram tier.</para>
/// </summary>
public static class TestLanguageModel
{
    /// <summary>
    /// Bigrams: context word, next word, log10 probability.
    /// Includes the sentence-start marker as a context so sentence openers can be tested.
    /// </summary>
    public const string Bigrams = """
        # test bigram model
        <s>	the	-0.3010
        <s>	this	-0.6021
        <s>	i	-0.9031
        i	am	-0.3010
        i	work	-0.6990
        am	looking	-0.3979
        am	working	-0.6990
        looking	at	-0.3010
        looking	for	-0.6021
        looking	forward	-1.0000
        thank	you	-0.1549
        """;

    /// <summary>Trigrams: two context words, next word, log10 probability.</summary>
    public const string Trigrams = """
        # test trigram model
        i	am	looking	-0.2218
        i	am	working	-0.6990
        am	looking	forward	-0.2218
        am	looking	for	-0.5229
        <s>	i	am	-0.3010
        """;

    /// <summary>
    /// Vocabulary for the Phase 2 tests, kept separate from <see cref="TestVocabulary"/> rather than added
    /// to it. The n-gram fixture needs words Phase 1's list never had ("looking", "forward", "thank"), and
    /// adding them there would quietly shift the frequency ordering that the Phase 1 ranking tests assert on.
    ///
    /// <para>Frequencies are spread widely and share no ties, so the unigram tier has one unambiguous
    /// ordering and a test that depends on it cannot pass or fail by accident.</para>
    /// </summary>
    private static readonly (string Word, long Frequency)[] Vocabulary =
    {
        ("the",      23_000_000_000L),
        ("to",       12_000_000_000L),
        ("and",      11_000_000_000L),
        ("a",         9_000_000_000L),
        ("i",         8_000_000_000L),
        ("you",       5_000_000_000L),
        ("for",       4_000_000_000L),
        ("at",        3_000_000_000L),
        ("this",      1_800_000_000L),
        ("work",        419_000_000L),
        ("looking",     200_000_000L),
        ("working",     150_000_000L),
        ("am",           90_000_000L),
        ("forward",      80_000_000L),
        ("thank",        60_000_000L),
        ("worth",        40_000_000L),
        ("zebra",            90_000L),
    };

    public static FrequencyDictionary BuildDictionary()
    {
        var text = string.Join(Environment.NewLine, Vocabulary.Select(e => $"{e.Word} {e.Frequency}"));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return FrequencyDictionary.LoadFromStream(stream);
    }

    public static NGramLanguageModel Build() => NGramLanguageModel.LoadFrom(
        new StringReader(Bigrams),
        new StringReader(Trigrams),
        BuildDictionary());

    /// <summary>A model with no n-gram data at all — everything must fall through to the unigram tier.</summary>
    public static NGramLanguageModel BuildEmpty() => NGramLanguageModel.Empty(BuildDictionary());

    /// <summary>A prediction engine wired to the fixture model, for the integration-level tests.</summary>
    public static PredictionEngine BuildEngine()
    {
        var dictionary = BuildDictionary();

        return new PredictionEngine(
            dictionary,
            SymSpellIndex.Build(dictionary, maxEditDistance: 2),
            languageModel: Build());
    }
}
