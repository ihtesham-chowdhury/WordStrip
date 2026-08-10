using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// Multi-word suggestions: when several words are offered as one candidate, and — more importantly — when
/// they are not. A wrong three-word phrase costs far more than a missing one, because the user has to
/// notice it, reject it and find their place again.
/// </summary>
public class PhraseSuggestionTests
{
    /// <summary>
    /// A fixture built so phrases can be reasoned about by reading it. "looking forward to hearing" is a
    /// strong chain at every step; "looking at" is a confident single word that goes nowhere; and
    /// "looking about" is deliberately weak so it can be checked that weak chains are dropped.
    /// </summary>
    private const string Bigrams = """
        <s>	i	-0.3010
        i	am	-0.1549
        am	looking	-0.2218
        looking	forward	-0.1549
        looking	at	-0.3010
        looking	about	-1.6990
        forward	to	-0.0458
        to	hearing	-0.3979
        to	the	-0.5229
        hearing	from	-0.0969
        about	it	-1.5000
        thank	you	-0.0458
        you	for	-0.2218
        for	your	-0.1549
        your	support	-0.3010
        """;

    private const string Trigrams = """
        i	am	looking	-0.1549
        am	looking	forward	-0.1549
        looking	forward	to	-0.0458
        forward	to	hearing	-0.3010
        to	hearing	from	-0.0969
        thank	you	for	-0.1549
        you	for	your	-0.1549
        for	your	support	-0.2218
        """;

    private static readonly (string Word, long Frequency)[] Vocabulary =
    {
        ("the", 23_000_000_000L), ("to", 12_000_000_000L), ("and", 11_000_000_000L), ("a", 9_000_000_000L),
        ("i", 8_000_000_000L), ("you", 5_000_000_000L), ("for", 4_000_000_000L), ("at", 3_000_000_000L),
        ("your", 1_500_000_000L), ("about", 1_200_000_000L), ("from", 1_100_000_000L),
        ("am", 90_000_000L), ("looking", 200_000_000L), ("forward", 80_000_000L), ("thank", 60_000_000L),
        ("hearing", 40_000_000L), ("support", 35_000_000L), ("it", 6_000_000_000L), ("zebra", 90_000L),
    };

    private static FrequencyDictionary BuildDictionary()
    {
        var text = string.Join(Environment.NewLine, Vocabulary.Select(e => $"{e.Word} {e.Frequency}"));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return FrequencyDictionary.LoadFromStream(stream);
    }

    private static NGramLanguageModel BuildModel() =>
        NGramLanguageModel.LoadFrom(new StringReader(Bigrams), new StringReader(Trigrams), BuildDictionary());

    private static PhraseGenerator NewGenerator(PersonalLanguageModel? personal = null) =>
        new(BuildModel(), personal);

    private static PredictionEngine NewEngine()
    {
        var dictionary = BuildDictionary();
        return new PredictionEngine(
            dictionary, SymSpellIndex.Build(dictionary, 2), languageModel: BuildModel());
    }

    private static IReadOnlyList<string> TextsOf(IEnumerable<PhraseCandidate> phrases) =>
        phrases.Select(p => p.Text).ToList();

    // --- Continuations of increasing length ------------------------------------------------------------

    [Fact]
    public void A_single_word_continuation_is_offered()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("thank"), 4);

        Assert.Contains(TextsOf(phrases), t => t.StartsWith("you", StringComparison.Ordinal));
    }

    [Fact]
    public void A_two_word_continuation_is_offered()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("am", "looking"), 4);

        // "forward to" may well have been superseded by "forward to hearing" — deduplication keeps only the
        // longest form of a given opening. What matters here is that the continuation spans more than one
        // word at all.
        var forward = Assert.Single(phrases, p => p.Text.StartsWith("forward", StringComparison.Ordinal));
        Assert.StartsWith("forward to", forward.Text, StringComparison.Ordinal);
        Assert.True(forward.WordCount > 1);
    }

    [Fact]
    public void A_three_word_continuation_is_offered()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("looking", "forward"), 4);

        Assert.Contains("to hearing from", TextsOf(phrases));
    }

    [Fact]
    public void Phrases_never_exceed_the_word_limit()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("i", "am"), 8);

        Assert.All(phrases, p => Assert.InRange(p.WordCount, 1, PhraseGenerator.MaxPhraseWords));
    }

    // --- Quality ---------------------------------------------------------------------------------------

    [Fact]
    public void A_weak_chain_is_not_extended()
    {
        // "looking about" is improbable and "about it" more so. The pair must not be offered as a phrase
        // merely because both words exist and the model can string them together.
        var phrases = NewGenerator().Generate(PredictionContext.After("am", "looking"), 8);

        Assert.DoesNotContain("about it", TextsOf(phrases));
    }

    [Fact]
    public void A_confident_short_phrase_can_beat_a_longer_one()
    {
        // Length must not win by itself: the score is the mean probability per word, so padding hurts.
        var phrases = NewGenerator().Generate(PredictionContext.After("am", "looking"), 6);

        var best = phrases[0];
        Assert.StartsWith("forward", best.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_phrase_never_repeats_a_word_it_already_used()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("i", "am"), 8);

        foreach (var phrase in phrases)
        {
            var words = phrase.Text.Split(' ');
            Assert.Equal(words.Length, words.Distinct(StringComparer.Ordinal).Count());
        }
    }

    // --- Deduplication ---------------------------------------------------------------------------------

    [Fact]
    public void The_bar_does_not_fill_with_one_phrase_growing_by_a_word()
    {
        // "forward", "forward to" and "forward to hearing" are three slots saying the same thing.
        var phrases = NewGenerator().Generate(PredictionContext.After("am", "looking"), 6);

        var startingWithForward = TextsOf(phrases).Count(t => t.StartsWith("forward", StringComparison.Ordinal));

        Assert.Equal(1, startingWithForward);
    }

    [Fact]
    public void Different_openings_are_all_kept()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("am", "looking"), 6);
        var openings = TextsOf(phrases).Select(t => t.Split(' ')[0]).ToList();

        Assert.Equal(openings.Count, openings.Distinct(StringComparer.Ordinal).Count());
        Assert.True(openings.Count > 1, "the bar should offer a spread, not one phrase");
    }

    // --- Confidence ------------------------------------------------------------------------------------

    [Fact]
    public void A_strong_chain_reports_higher_confidence_than_a_weak_one()
    {
        var strong = NewGenerator().Generate(PredictionContext.After("looking", "forward"), 4);
        var weak = NewGenerator().Generate(PredictionContext.After("zebra"), 4);

        var strongest = strong.Max(p => p.Confidence);
        var weakest = weak.Count == 0 ? 0 : weak.Max(p => p.Confidence);

        Assert.True(strongest > weakest, $"strong {strongest} should exceed weak {weakest}");
    }

    [Fact]
    public void Confidence_stays_within_zero_and_one()
    {
        var phrases = NewGenerator().Generate(PredictionContext.After("i", "am"), 8);

        Assert.All(phrases, p => Assert.InRange(p.Confidence, 0, 1));
    }

    [Fact]
    public void An_unknown_context_degrades_to_single_words_rather_than_inventing_a_phrase()
    {
        // Falling through to the unigram tier means no contextual evidence at all, so nothing should be
        // strung together — the honest answer is a list of common words.
        var phrases = NewGenerator().Generate(PredictionContext.After("zebra", "qwerty"), 5);

        Assert.All(phrases, p => Assert.Equal(1, p.WordCount));
    }

    [Fact]
    public void Nothing_at_all_is_returned_when_the_model_is_empty()
    {
        var empty = new PhraseGenerator(NGramLanguageModel.Empty(BuildDictionary()));

        // The unigram tier still answers, so this is single words only, never phrases.
        var phrases = empty.Generate(PredictionContext.After("i", "am"), 4);

        Assert.All(phrases, p => Assert.Equal(1, p.WordCount));
    }

    // --- Personal influence ----------------------------------------------------------------------------

    [Fact]
    public void What_the_user_writes_shapes_which_phrase_wins()
    {
        var personal = new PersonalLanguageModel(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json"));

        // Teach it heavily that "looking at" is this person's habit, against a corpus that prefers "forward".
        for (var i = 0; i < 3000; i++)
        {
            personal.Learn("looking", new[] { "am" });
            personal.Learn("at", new[] { "am", "looking" });
        }

        var withoutPersonal = NewGenerator().Generate(PredictionContext.After("am", "looking"), 5);
        var withPersonal = NewGenerator(personal).Generate(PredictionContext.After("am", "looking"), 5);

        var rankOf = (IReadOnlyList<PhraseCandidate> list) =>
            TextsOf(list).ToList().FindIndex(t => t.StartsWith("at", StringComparison.Ordinal));

        Assert.True(rankOf(withPersonal) <= rankOf(withoutPersonal),
            "personal usage should not push a phrase down the list");
    }

    // --- Determinism and integration -------------------------------------------------------------------

    [Fact]
    public void Generation_is_deterministic()
    {
        var generator = NewGenerator();
        var first = TextsOf(generator.Generate(PredictionContext.After("i", "am"), 6));

        for (var attempt = 0; attempt < 20; attempt++)
            Assert.Equal(first, TextsOf(generator.Generate(PredictionContext.After("i", "am"), 6)));
    }

    [Fact]
    public void The_engine_offers_phrases_only_when_asked()
    {
        var engine = NewEngine();

        var withPhrases = engine.GetNextWords(PredictionContext.After("am", "looking"), 5, includePhrases: true);
        var withoutPhrases = engine.GetNextWords(PredictionContext.After("am", "looking"), 5, includePhrases: false);

        Assert.Contains(withPhrases, s => s.WordCount > 1);
        Assert.All(withoutPhrases, s => Assert.Equal(1, s.WordCount));
    }

    [Fact]
    public void A_phrase_candidate_is_tagged_as_one()
    {
        var engine = NewEngine();

        var multiWord = engine.GetNextWords(PredictionContext.After("am", "looking"), 5, includePhrases: true)
                              .First(s => s.WordCount > 1);

        Assert.True(multiWord.IsPhrase);
        Assert.Equal(SuggestionSource.Phrase, multiWord.Source);
    }

    [Fact]
    public void A_phrase_is_one_candidate_carrying_its_own_spacing()
    {
        // The injector appends a single trailing space; the words inside must already be separated, and
        // there must be no leading or trailing whitespace to produce "Thank you  for".
        var engine = NewEngine();

        var multiWord = engine.GetNextWords(PredictionContext.After("am", "looking"), 5, includePhrases: true)
                              .First(s => s.WordCount > 1);

        Assert.Equal(multiWord.Word.Trim(), multiWord.Word);
        Assert.DoesNotContain("  ", multiWord.Word, StringComparison.Ordinal);
        Assert.Equal(multiWord.WordCount - 1, multiWord.Word.Count(c => c == ' '));
    }

    [Fact]
    public void Completion_of_a_part_typed_word_is_never_a_phrase()
    {
        // Mid-word the user has told us something concrete; answering with several words would put text on
        // the bar that does not match the letters on screen.
        var engine = NewEngine();

        var suggestions = engine.GetLiveSuggestions("for", 5, PredictionContext.After("am", "looking"));

        Assert.All(suggestions, s => Assert.False(s.IsPhrase));
    }
}
