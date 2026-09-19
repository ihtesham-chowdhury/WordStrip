using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// An engine built for the interaction-model tests, where what matters is knowing in advance which
/// completion is confident and which prediction comes first.
///
/// <para>Chosen so each case has one unambiguous answer: "looki" has exactly one completion; "hel" has a clear
/// favourite ("help", eight times "held"); "wor" is a near tie ("world" against "work") and must never be
/// completed by Space; "look", "can" and "work" are finished words. After "looking" the model ranks "for",
/// then "at", then "back", with "forward" last.</para>
/// </summary>
public static class InteractionTestEngine
{
    private static readonly (string Word, long Frequency)[] Vocabulary =
    {
        ("the",      23_000_000_000L),
        ("i",         8_000_000_000L),
        ("for",       4_000_000_000L),
        ("at",        3_000_000_000L),
        ("can",       2_000_000_000L),
        ("help",        611_000_000L),
        ("world",       431_000_000L),
        ("work",        419_000_000L),
        ("look",        400_000_000L),
        ("back",        300_000_000L),
        ("looking",     200_000_000L),
        ("am",           90_000_000L),
        ("forward",      80_000_000L),
        ("held",         76_000_000L),
        ("looked",       60_000_000L),
        ("looks",        50_000_000L),
        ("hello",        32_000_000L),
        ("candle",        5_000_000L),
        ("tehran",        2_000_000L),
        ("london",       90_000_000L),
        ("in",        9_000_000_000L),
        ("hello",        32_000_000L),
        ("how",       1_500_000_000L),
        ("well",        900_000_000L),
        ("were",        900_000_000L),
        ("ill",          50_000_000L),
    };

    private const string Bigrams = """
        i	am	-0.3010
        am	looking	-0.2000
        looking	for	-0.2000
        looking	at	-0.4000
        looking	back	-0.7000
        looking	forward	-0.9000
        for	the	-0.3000
        """;

    private const string Trigrams = """
        i	am	looking	-0.2218
        am	looking	for	-0.2000
        am	looking	at	-0.4000
        am	looking	back	-0.7000
        am	looking	forward	-0.9000
        """;

    public static FrequencyDictionary BuildDictionary()
    {
        var text = string.Join(Environment.NewLine, Vocabulary.Select(e => $"{e.Word} {e.Frequency}"));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return FrequencyDictionary.LoadFromStream(stream);
    }

    public static PredictionEngine Build(PersonalVocabularyStore? personalVocabulary = null)
    {
        var dictionary = BuildDictionary();
        var model = NGramLanguageModel.LoadFrom(new StringReader(Bigrams), new StringReader(Trigrams), dictionary);

        return new PredictionEngine(
            dictionary,
            SymSpellIndex.Build(dictionary, maxEditDistance: 2),
            languageModel: model,
            personalVocabulary: personalVocabulary,
            emoji: EmojiSuggester.Default);
    }
}
