using WordStrip.Core.Prediction;
using WordStrip.Core.Prediction.NGram;

namespace WordStrip.Core.Tests;

/// <summary>
/// The language model itself: what it predicts, and — mostly — how it behaves when it does not know.
/// A model that answers well only when it has seen the exact context is easy; almost every real keystroke
/// lands somewhere it has not, so the backoff path is the one that decides whether the bar is useful.
/// </summary>
public class NGramLanguageModelTests
{
    private readonly NGramLanguageModel _model = TestLanguageModel.Build();

    private static IReadOnlyList<string> Words(IReadOnlyList<NGramPrediction> predictions) =>
        predictions.Select(p => p.Word).ToList();

    // --- Trigram ------------------------------------------------------------------------------------

    [Fact]
    public void A_known_trigram_context_predicts_its_continuations_best_first()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("i", "am"), 2);

        Assert.Equal(new[] { "looking", "working" }, Words(predictions));
    }

    [Fact]
    public void A_trigram_hit_is_reported_as_a_trigram()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("i", "am"), 1);

        Assert.Equal(NGramOrder.Trigram, predictions[0].Order);
    }

    [Fact]
    public void The_trigram_uses_both_context_words_not_just_the_last()
    {
        // "looking" alone predicts "at" first. Preceded by "am", the trigram takes over and "forward" wins.
        // If this ever returns "at", the model is quietly ignoring the older context word.
        var afterOneWord = _model.GetNextWordCandidates(PredictionContext.After("looking"), 1);
        var afterTwoWords = _model.GetNextWordCandidates(PredictionContext.After("am", "looking"), 1);

        Assert.Equal("at", afterOneWord[0].Word);
        Assert.Equal("forward", afterTwoWords[0].Word);
    }

    // --- Backoff ------------------------------------------------------------------------------------

    [Fact]
    public void An_unknown_trigram_context_backs_off_to_the_bigram()
    {
        // "you looking" is not a trigram context in the fixture, so the last word alone has to carry it.
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("you", "looking"), 1);

        Assert.Equal("at", predictions[0].Word);
        Assert.Equal(NGramOrder.Bigram, predictions[0].Order);
    }

    [Fact]
    public void An_unknown_bigram_context_backs_off_to_word_frequency()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("zebra"), 3);

        Assert.Equal(new[] { "the", "to", "and" }, Words(predictions));
        Assert.All(predictions, p => Assert.Equal(NGramOrder.Unigram, p.Order));
    }

    [Fact]
    public void Backing_off_tops_up_a_thin_trigram_rather_than_stopping_short()
    {
        // "am looking" has only two trigram continuations. Asked for five, the model must keep going into
        // the lower orders — a bar that shows two words because the corpus was thin is a worse answer than
        // two good words followed by three plausible ones.
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("am", "looking"), 5);

        Assert.Equal(5, predictions.Count);
        Assert.Equal(new[] { "forward", "for" }, Words(predictions).Take(2));
        Assert.Equal(NGramOrder.Trigram, predictions[0].Order);
        Assert.Equal(NGramOrder.Trigram, predictions[1].Order);
        Assert.NotEqual(NGramOrder.Trigram, predictions[2].Order);
    }

    [Fact]
    public void A_word_is_never_offered_twice_when_orders_overlap()
    {
        // "for" is both a trigram continuation of "am looking" and a bigram continuation of "looking".
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("am", "looking"), 8);

        Assert.Equal(Words(predictions).Distinct().Count(), predictions.Count);
    }

    [Fact]
    public void Every_step_down_the_orders_costs_a_fixed_penalty()
    {
        var trigram = _model.GetLogScore(PredictionContext.After("i", "am"), "looking");
        var bigram = _model.GetLogScore(PredictionContext.After("you", "am"), "looking");

        // Same word, same immediate predecessor; the only difference is that one matched a trigram and the
        // other had to back off. The gap must be exactly one penalty.
        Assert.NotNull(trigram);
        Assert.NotNull(bigram);
        Assert.Equal(-0.2218, trigram!.Value, precision: 4);
        Assert.Equal(NGramLanguageModel.BackoffPenalty + -0.3979, bigram!.Value, precision: 4);
    }

    [Fact]
    public void Trigram_evidence_always_outscores_bigram_evidence_which_always_outscores_frequency()
    {
        var context = PredictionContext.After("i", "am");

        var trigram = _model.GetLogScore(context, "looking")!.Value;
        var bigram = _model.GetLogScore(PredictionContext.After("looking"), "at")!.Value;
        var unigram = _model.GetLogScore(PredictionContext.After("zebra"), "the")!.Value;

        Assert.True(trigram > bigram, $"trigram {trigram} should beat bigram {bigram}");
        Assert.True(bigram > unigram, $"bigram {bigram} should beat unigram {unigram}");
    }

    [Fact]
    public void A_word_nobody_has_ever_seen_scores_nothing_at_all()
    {
        Assert.Null(_model.GetLogScore(PredictionContext.After("i", "am"), "qwertyuiop"));
        Assert.Equal(NGramOrder.None, _model.GetMatchedOrder(PredictionContext.After("i", "am"), "qwertyuiop"));
    }

    // --- Sentence boundaries and punctuation ---------------------------------------------------------

    [Fact]
    public void A_sentence_start_predicts_sentence_openers_rather_than_common_words()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.AtSentenceStart(), 3);

        // "the", "this", "i" are the fixture's openers. Plain frequency would have put "to" and "and" high.
        Assert.Equal(new[] { "the", "this", "i" }, Words(predictions));
        Assert.All(predictions, p => Assert.Equal(NGramOrder.Bigram, p.Order));
    }

    [Fact]
    public void The_word_before_a_full_stop_is_not_used_to_predict_the_word_after_it()
    {
        // The caller reports a sentence start; whatever came before must be ignored entirely, because the
        // last word of one sentence says nothing about how the next one opens.
        var context = new PredictionContext(string.Empty, new[] { "i", "am" }, IsSentenceStart: true);

        var predictions = _model.GetNextWordCandidates(context, 3);

        Assert.Equal(new[] { "the", "this", "i" }, Words(predictions));
    }

    [Fact]
    public void One_word_into_a_sentence_the_context_is_the_marker_plus_that_word()
    {
        // Having typed "I" at the start of a sentence, the trigram "<s> i am" applies.
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("i"), 1);

        Assert.Equal("am", predictions[0].Word);
        Assert.Equal(NGramOrder.Trigram, predictions[0].Order);
    }

    // --- Capitalisation and normalisation -------------------------------------------------------------

    [Fact]
    public void Context_words_match_regardless_of_how_they_were_capitalised()
    {
        var lower = _model.GetNextWordCandidates(PredictionContext.After("i", "am"), 1);
        var upper = _model.GetNextWordCandidates(PredictionContext.After("I", "AM"), 1);
        var mixed = _model.GetNextWordCandidates(PredictionContext.After("I", "Am"), 1);

        Assert.Equal("looking", lower[0].Word);
        Assert.Equal("looking", upper[0].Word);
        Assert.Equal("looking", mixed[0].Word);
    }

    [Fact]
    public void Punctuation_stuck_to_a_context_word_does_not_stop_it_matching()
    {
        // The typing layer hands over what it captured; a stray quote or comma must not blind the model.
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("\"i", "am,"), 1);

        Assert.Equal("looking", predictions[0].Word);
    }

    [Fact]
    public void A_context_word_with_no_letters_is_treated_as_no_context()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.After("---"), 3);

        Assert.Equal(new[] { "the", "this", "i" }, Words(predictions));
    }

    // --- Empty and degenerate input -------------------------------------------------------------------

    [Fact]
    public void Empty_context_predicts_sentence_openers()
    {
        var predictions = _model.GetNextWordCandidates(PredictionContext.Empty, 3);

        Assert.Equal(new[] { "the", "this", "i" }, Words(predictions));
    }

    [Fact]
    public void Asking_for_no_results_returns_none()
    {
        Assert.Empty(_model.GetNextWordCandidates(PredictionContext.After("i", "am"), 0));
        Assert.Empty(_model.GetNextWordCandidates(PredictionContext.After("i", "am"), -1));
    }

    [Fact]
    public void A_model_with_no_data_still_answers_from_word_frequency()
    {
        // This is what the app does when the model files are missing: it must degrade to Phase 1 behaviour,
        // not to an empty bar.
        var empty = TestLanguageModel.BuildEmpty();

        var predictions = empty.GetNextWordCandidates(PredictionContext.After("i", "am"), 3);

        Assert.Equal(new[] { "the", "to", "and" }, Words(predictions));
        Assert.All(predictions, p => Assert.Equal(NGramOrder.Unigram, p.Order));
    }

    // --- Determinism ----------------------------------------------------------------------------------

    [Fact]
    public void The_same_context_always_produces_the_same_answer()
    {
        var first = Words(_model.GetNextWordCandidates(PredictionContext.After("am", "looking"), 6));

        for (var attempt = 0; attempt < 20; attempt++)
            Assert.Equal(first, Words(_model.GetNextWordCandidates(PredictionContext.After("am", "looking"), 6)));
    }

    [Fact]
    public void Reloading_the_same_files_produces_the_same_model()
    {
        var reloaded = TestLanguageModel.Build();

        Assert.Equal(
            Words(_model.GetNextWordCandidates(PredictionContext.After("i", "am"), 5)),
            Words(reloaded.GetNextWordCandidates(PredictionContext.After("i", "am"), 5)));
    }

    [Fact]
    public void Continuations_are_ordered_by_probability_even_if_the_file_is_not()
    {
        // A hand-edited or externally generated file must not be able to change what the user sees just by
        // listing its lines in a different order.
        var shuffled = NGramLanguageModel.LoadFrom(
            new StringReader("looking\tforward\t-1.0000\nlooking\tat\t-0.3010\nlooking\tfor\t-0.6021"),
            null,
            TestLanguageModel.BuildDictionary());

        var predictions = shuffled.GetNextWordCandidates(PredictionContext.After("looking"), 3);

        Assert.Equal(new[] { "at", "for", "forward" }, Words(predictions));
    }

    // --- Parsing --------------------------------------------------------------------------------------

    [Fact]
    public void Comments_and_blank_lines_are_ignored()
    {
        var model = NGramLanguageModel.LoadFrom(
            new StringReader("# a comment\n\nlooking\tat\t-0.3010\n"),
            null,
            TestLanguageModel.BuildDictionary());

        Assert.Equal(1, model.BigramContextCount);
        Assert.Equal("at", model.GetNextWordCandidates(PredictionContext.After("looking"), 1)[0].Word);
    }

    [Fact]
    public void Malformed_lines_are_skipped_rather_than_taking_the_model_down()
    {
        var model = NGramLanguageModel.LoadFrom(
            new StringReader("looking\n\nlooking\tat\nlooking\tat\tnot-a-number\nlooking\tfor\t-0.6021\n"),
            null,
            TestLanguageModel.BuildDictionary());

        var predictions = model.GetNextWordCandidates(PredictionContext.After("looking"), 5);

        Assert.Equal("for", predictions[0].Word);
    }
}
