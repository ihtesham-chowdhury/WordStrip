using WordStrip.Core.Prediction;

namespace WordStrip.Core.Tests;

/// <summary>
/// A small, hand-written vocabulary shared by the prediction tests.
///
/// <para>Deliberately not the real 60,000-word dictionary: tests should assert on behaviour that is obvious
/// from reading them, and with a controlled word list the expected ordering can be reasoned about by hand.
/// Frequencies are spread across several orders of magnitude so ranking bands are actually exercised.</para>
/// </summary>
public static class TestVocabulary
{
    public static readonly (string Word, long Frequency)[] Entries =
    {
        ("the",      23_000_000_000L),
        ("to",       12_000_000_000L),
        ("and",      12_000_000_000L),
        ("a",         9_000_000_000L),
        ("that",      3_400_000_000L),
        ("this",      1_800_000_000L),
        ("there",       900_000_000L),
        ("their",       780_000_000L),
        ("them",        420_000_000L),
        ("then",        390_000_000L),
        ("these",       350_000_000L),
        ("world",       431_000_000L),
        ("work",        419_000_000L),
        ("working",     147_000_000L),
        ("word",         98_000_000L),
        ("words",        62_000_000L),
        ("works",        60_000_000L),
        ("worth",        40_000_000L),
        ("receive",      88_000_000L),
        ("received",     90_000_000L),
        ("relieve",       4_000_000L),
        ("recipe",        3_000_000L),
        ("definitely",   15_000_000L),
        ("hello",        32_000_000L),
        ("help",        611_000_000L),
        ("held",         76_000_000L),
        ("island",       28_000_000L),
        ("islands",      12_000_000L),
        ("is",        4_100_000_000L),
        ("issue",        95_000_000L),
        ("issues",      120_000_000L),
        ("zebra",            90_000L),
    };

    public static FrequencyDictionary BuildDictionary()
    {
        var text = string.Join(Environment.NewLine, Entries.Select(e => $"{e.Word} {e.Frequency}"));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return FrequencyDictionary.LoadFromStream(stream);
    }

    public static PredictionEngine BuildEngine(int maxEditDistance = 2)
    {
        var dictionary = BuildDictionary();
        return new PredictionEngine(dictionary, SymSpellIndex.Build(dictionary, maxEditDistance));
    }
}

/// <summary>Builds the engine once for the whole test class — index construction is the expensive part.</summary>
public sealed class EngineFixture
{
    public PredictionEngine Engine { get; } = TestVocabulary.BuildEngine();
    public FrequencyDictionary Dictionary { get; } = TestVocabulary.BuildDictionary();
}
