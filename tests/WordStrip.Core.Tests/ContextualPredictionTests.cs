using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// The two prediction modes and the ranking that joins them: context must reorder completions without ever
/// overruling what the user has actually typed, and must lead entirely once there is nothing left to
/// complete.
/// </summary>
public class ContextualPredictionTests
{
    private readonly PredictionEngine _engine = TestLanguageModel.BuildEngine();
    private readonly ContextualRanker _ranker = new(TestLanguageModel.Build());

    private static IReadOnlyList<string> Words(IReadOnlyList<Suggestion> suggestions) =>
        suggestions.Select(s => s.Word).ToList();

    // --- Next-word mode -------------------------------------------------------------------------------

    [Fact]
    public void Between_words_the_bar_shows_what_is_likely_to_come_next()
    {
        var suggestions = _engine.GetNextWords(PredictionContext.After("i", "am"), 2);

        Assert.Equal(new[] { "looking", "working" }, Words(suggestions));
    }

    [Fact]
    public void Context_beats_raw_frequency_between_words()
    {
        // "the" is nearly three hundred times more frequent than "looking" in the fixture vocabulary. With
        // no context it would win outright; after "i am" it must not appear at all in the top two.
        var suggestions = _engine.GetNextWords(PredictionContext.After("i", "am"), 2);

        Assert.DoesNotContain("the", Words(suggestions));
    }

    [Fact]
    public void With_no_context_at_all_the_bar_still_fills_up()
    {
        var suggestions = _engine.GetNextWords(PredictionContext.Empty, 4);

        Assert.Equal(4, suggestions.Count);
    }

    // --- Current-word mode ----------------------------------------------------------------------------

    [Fact]
    public void While_typing_a_word_only_words_matching_what_was_typed_are_offered()
    {
        // "looking" is the single most likely word after "i am", but the user is typing "wor" and offering
        // it would put a word on the bar that does not match the letters on screen.
        var suggestions = _engine.GetLiveSuggestions("wor", 5, PredictionContext.After("i", "am"));

        Assert.DoesNotContain("looking", Words(suggestions));

        // Every candidate is still derived from what was typed: a completion of it, or a fuzzy repair of it
        // when prefix matching came up short. None is there purely because context liked it.
        Assert.All(suggestions, s => Assert.True(
            s.Word.StartsWith("wor", StringComparison.Ordinal) || s.EditDistance > 0,
            $"'{s.Word}' neither completes nor repairs 'wor' (source {s.Source}, distance {s.EditDistance})"));
    }

    [Fact]
    public void Context_reorders_completions_of_the_word_being_typed()
    {
        // "work" is nearly three times as frequent as "working", so frequency alone ranks it first. After
        // "i am" the trigram favours "working", which should carry it past its more common sibling.
        //
        // The prefix is "wor" rather than "work" on purpose: typing "work" in full makes it an exact
        // dictionary match, and an exact match sits a whole band above a mere completion where no amount of
        // context can reach it. That is the deliberate rule asserted below in
        // Context_cannot_lift_a_candidate_out_of_its_band — context reorders peers, it does not overrule
        // what the user has demonstrably finished typing.
        var withoutContext = _engine.GetLiveSuggestions("wor", 3, PredictionContext.Empty);
        var withContext = _engine.GetLiveSuggestions("wor", 3, PredictionContext.After("i", "am"));

        Assert.Equal("work", Words(withoutContext)[0]);
        Assert.Equal("working", Words(withContext)[0]);
    }

    [Fact]
    public void Completion_without_a_context_argument_behaves_exactly_as_it_did_before()
    {
        var implicitly_ = _engine.GetLiveSuggestions("wor", 4);
        var explicitly = _engine.GetLiveSuggestions("wor", 4, PredictionContext.Empty);

        Assert.Equal(Words(explicitly), Words(implicitly_));
    }

    // --- The ranking contract -------------------------------------------------------------------------

    [Fact]
    public void Context_cannot_lift_a_candidate_out_of_its_band()
    {
        // The bands Phase 1 established are 100 apart and the frequency term reaches about 10. If the
        // context bonus could ever exceed that gap, a merely likely word could outrank one the user has
        // already typed in full — the bar would start fighting the typist.
        var context = PredictionContext.After("i", "am");

        foreach (var word in new[] { "looking", "working", "the", "at", "forward", "zebra" })
            Assert.InRange(_ranker.ContextBonus(word, context), 0, ContextualRanker.MaxContextBonus);
    }

    [Fact]
    public void An_exact_match_still_outranks_a_contextually_likelier_completion()
    {
        // Typing "work" exactly: "work" is an ExactWord, "working" only a PrefixCompletion that context
        // happens to like. Bands must keep the exact match on top... except that the user typing every
        // letter of "work" is itself the strongest signal there is.
        var candidates = new[]
        {
            new Suggestion("work", 419_000_000, 0, SuggestionSource.ExactWord),
            new Suggestion("working", 150_000_000, 0, SuggestionSource.PrefixCompletion),
        };

        var ranked = _ranker.Rank(new RankingContext("work", PredictionContext.After("i", "am")), candidates, 2);

        Assert.Equal("work", ranked[0].Word);
    }

    [Fact]
    public void A_word_the_model_knows_nothing_about_is_scored_exactly_as_before()
    {
        Assert.Equal(0, _ranker.ContextBonus("zebra", PredictionContext.After("i", "am")));
    }

    [Fact]
    public void Trigram_evidence_earns_a_bigger_bonus_than_bigram_evidence()
    {
        var fromTrigram = _ranker.ContextBonus("looking", PredictionContext.After("i", "am"));
        var fromBigram = _ranker.ContextBonus("at", PredictionContext.After("looking"));

        Assert.True(fromTrigram > fromBigram, $"trigram bonus {fromTrigram} should exceed bigram bonus {fromBigram}");
    }

    [Fact]
    public void Ranking_is_deterministic()
    {
        var context = PredictionContext.After("i", "am");
        var first = Words(_engine.GetNextWords(context, 6));

        for (var attempt = 0; attempt < 20; attempt++)
            Assert.Equal(first, Words(_engine.GetNextWords(context, 6)));
    }

    // --- Phase 1 must be untouched --------------------------------------------------------------------

    [Fact]
    public void An_engine_with_no_language_model_ranks_exactly_as_frequency_alone_would()
    {
        // The safety net for "Phase 2 changed nothing that used to work": with no model, the contextual
        // ranker's bonus is always zero, so its output must match Phase 1's ranker candidate for candidate.
        var dictionary = TestLanguageModel.BuildDictionary();
        var candidates = new[]
        {
            new Suggestion("work", 419_000_000, 0, SuggestionSource.PrefixCompletion),
            new Suggestion("working", 150_000_000, 0, SuggestionSource.PrefixCompletion),
            new Suggestion("worth", 40_000_000, 0, SuggestionSource.PrefixCompletion),
        };

        var contextual = new ContextualRanker(NGramLanguageModel.Empty(dictionary))
            .Rank(new RankingContext("wor", PredictionContext.After("i", "am")), candidates, 3);
        var frequency = new FrequencyRanker().Rank(new RankingContext("wor"), candidates, 3);

        Assert.Equal(Words(frequency), Words(contextual));
    }

    [Fact]
    public void Autocorrection_is_unaffected_by_context()
    {
        // Autocorrect runs on a finished word against the spelling index and has nothing to do with the
        // language model. Guarding it here because it would be an easy thing to break by accident.
        var correction = _engine.GetAutocorrection("workin", minFrequencyForConfidence: 1000);

        Assert.NotNull(correction);
        Assert.Equal("working", correction!.Value.Word);
    }
}
