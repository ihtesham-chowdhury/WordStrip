using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Suggestions;

namespace WordStrip.Core.Tests;

/// <summary>
/// The pieces that keep the bar calm: ranking hysteresis and slot identity, the completion policy's
/// refusals, render coalescing, and the typing session's exact record of what WordStrip inserted.
/// </summary>
public class StabilityTests
{
    private static Suggestion S(string word, double score, SuggestionSource source = SuggestionSource.PrefixCompletion) =>
        new(word, 1, 0, source, Score: score);

    private static string[] Words(IEnumerable<Suggestion> list) => list.Select(s => s.Word).ToArray();

    // --- Ranking hysteresis -----------------------------------------------------------------------------

    [Fact]
    public void A_small_score_change_does_not_reorder_the_bar()
    {
        var previous = new[] { S("going", 10.0), S("gone", 9.9), S("got", 9.0) };
        var next = new[] { S("gone", 10.1), S("going", 10.0), S("got", 9.0) };

        var shown = CandidateStabilizer.Stabilize(previous, next, hysteresis: 0.35);

        Assert.Equal(new[] { "going", "gone", "got" }, Words(shown));
    }

    [Fact]
    public void A_decisive_score_change_does_reorder_the_bar()
    {
        var previous = new[] { S("going", 10.0), S("gone", 9.9), S("got", 9.0) };
        var next = new[] { S("gone", 11.0), S("going", 10.0), S("got", 9.0) };

        var shown = CandidateStabilizer.Stabilize(previous, next, hysteresis: 0.35);

        Assert.Equal("gone", shown[0].Word);
    }

    [Fact]
    public void Words_that_stay_on_the_bar_keep_their_slots_and_a_newcomer_takes_the_empty_one()
    {
        var previous = new[] { S("going", 10.0), S("got", 9.5), S("gone", 9.4), S("good", 9.3) };
        var next = new[] { S("going", 10.0), S("great", 9.6), S("got", 9.5), S("gone", 9.4) };

        var shown = CandidateStabilizer.Stabilize(previous, next, hysteresis: 0.35);

        Assert.Equal(new[] { "going", "got", "gone", "great" }, Words(shown));
    }

    [Fact]
    public void An_emoji_stays_last_whatever_the_order_above_it()
    {
        var previous = new[] { S("happy", 10.0), S("hope", 9.9), S("😊", 0, SuggestionSource.Emoji) };
        var next = new[] { S("hope", 10.1), S("happy", 10.0), S("😊", 0, SuggestionSource.Emoji) };

        var shown = CandidateStabilizer.Stabilize(previous, next, hysteresis: 0.35);

        Assert.Equal("😊", shown[^1].Word);
    }

    [Fact]
    public void Zero_hysteresis_shows_the_model_order_unchanged()
    {
        var previous = new[] { S("going", 10.0), S("gone", 9.9) };
        var next = new[] { S("gone", 10.1), S("going", 10.0) };

        Assert.Same(next, CandidateStabilizer.Stabilize(previous, next, hysteresis: 0));
    }

    [Fact]
    public void Stabilising_never_adds_or_loses_a_candidate()
    {
        var previous = new[] { S("a", 5), S("b", 4), S("c", 3), S("d", 2) };
        var next = new[] { S("e", 9), S("c", 3.1), S("a", 5.2), S("f", 1) };

        var shown = CandidateStabilizer.Stabilize(previous, next, hysteresis: 0.35);

        Assert.Equal(Words(next).OrderBy(w => w), Words(shown).OrderBy(w => w));
    }

    // --- Completion policy -------------------------------------------------------------------------------

    private static readonly CompletionThresholds Defaults = new(3, 0.6, 0.25);

    [Fact]
    public void A_fuzzy_repair_is_never_committed_by_space()
    {
        var shown = new[] { S("world", 208, SuggestionSource.FuzzyMatch) };

        Assert.Null(CompletionPolicy.SelectForBoundary("wrld", shown, _ => false, Defaults));
    }

    [Fact]
    public void An_emoji_is_never_committed_by_space()
    {
        var shown = new[] { S("😊", 0, SuggestionSource.Emoji) };

        Assert.Null(CompletionPolicy.SelectForBoundary("smi", shown, _ => false, Defaults));
    }

    [Fact]
    public void A_prefix_shorter_than_the_minimum_is_left_alone()
    {
        var shown = new[] { S("looking", 250) };

        Assert.Null(CompletionPolicy.SelectForBoundary("lo", shown, _ => false, Defaults));
    }

    [Fact]
    public void A_first_slot_held_in_place_by_hysteresis_but_no_longer_ahead_is_not_committed()
    {
        // Displayed first, but the model now scores the runner-up higher: the margin is negative.
        var shown = new[] { S("looking", 10.0), S("looked", 10.2) };

        Assert.Null(CompletionPolicy.SelectForBoundary("look", shown, _ => false, Defaults));
    }

    [Fact]
    public void Confidence_is_the_first_candidates_share_of_the_combined_likelihood()
    {
        // Scores are log10-scaled: a one-point lead is ten times the likelihood.
        var shown = new[] { S("a", 11), S("b", 10) };

        var confidence = CompletionPolicy.Confidence(shown, out var margin);

        Assert.Equal(10.0 / 11.0, confidence, precision: 6);
        Assert.Equal(1.0, margin, precision: 6);
    }

    // --- One word per Tab --------------------------------------------------------------------------------

    [Fact]
    public void The_first_prediction_is_always_a_single_word()
    {
        var predictions = new[]
        {
            S("to the", 150, SuggestionSource.Phrase), S("for", 148, SuggestionSource.FrequentWord),
            S("at", 146, SuggestionSource.FrequentWord),
        };

        var shaped = SuggestionController.ShapePredictions(predictions);

        Assert.Equal(new[] { "for", "to the", "at" }, Words(shaped));
    }

    [Fact]
    public void With_only_phrases_the_first_slot_is_the_first_word_of_the_best_one()
    {
        var predictions = new[] { S("to the", 150, SuggestionSource.Phrase), S("for a bit", 140, SuggestionSource.Phrase) };
        // "for a bit" is 9 characters: short enough to keep.

        var shaped = SuggestionController.ShapePredictions(predictions);

        Assert.Equal("to", shaped[0].Word);
        Assert.False(shaped[0].IsPhrase);
    }

    [Fact]
    public void A_phrase_too_long_for_a_slot_is_not_offered()
    {
        var predictions = new[] { S("for", 150, SuggestionSource.FrequentWord), S("forward to seeing you", 140, SuggestionSource.Phrase) };

        Assert.Equal(new[] { "for" }, Words(SuggestionController.ShapePredictions(predictions)));
    }

    // --- Render coalescing -------------------------------------------------------------------------------

    [Fact]
    public void A_burst_of_updates_is_delivered_once_as_the_latest()
    {
        var scheduled = new List<Action>();
        var delivered = new List<int>();
        var coalescer = new LatestValueCoalescer<int>(scheduled.Add, delivered.Add);

        coalescer.Post(1);
        coalescer.Post(2);
        coalescer.Post(3);

        Assert.Single(scheduled);
        scheduled[0]();

        Assert.Equal(new[] { 3 }, delivered);
        Assert.Equal(2, coalescer.Coalesced);
    }

    [Fact]
    public void A_stale_delivery_can_never_overwrite_a_newer_one()
    {
        var scheduled = new List<Action>();
        var delivered = new List<int>();
        var coalescer = new LatestValueCoalescer<int>(scheduled.Add, delivered.Add);

        coalescer.Post(1);
        coalescer.Post(2);
        coalescer.Flush();          // delivers 2 early
        scheduled[0]();             // the delivery originally scheduled for 1 runs late

        Assert.Equal(new[] { 2 }, delivered);
    }

    [Fact]
    public void An_update_after_a_delivery_is_scheduled_again()
    {
        var scheduled = new List<Action>();
        var delivered = new List<int>();
        var coalescer = new LatestValueCoalescer<int>(scheduled.Add, delivered.Add);

        coalescer.Post(1);
        scheduled[0]();
        coalescer.Post(2);
        scheduled[1]();

        Assert.Equal(new[] { 1, 2 }, delivered);
    }

    // --- Exact span tracking in the typing session -------------------------------------------------------

    private static TypingSession NewSession() => new(new LowLevelKeyboardHook(), new LowLevelMouseHook());

    [Fact]
    public void A_completion_inside_the_word_leaves_the_history_untouched()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "i am looki");

        session.NoteTextReplaced("looki", "looking");

        Assert.Equal("looking", session.CurrentWord);
        Assert.Equal(new[] { "i", "am" }, session.RecentWords);
    }

    [Fact]
    public void A_prediction_after_a_finished_word_moves_that_word_into_history()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "am looking");

        session.NoteTextReplaced(string.Empty, " for");

        Assert.Equal("for", session.CurrentWord);
        Assert.Equal(new[] { "am", "looking" }, session.RecentWords);
    }

    [Fact]
    public void Replacing_a_multi_word_insertion_takes_back_every_word_of_it()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "looking ");
        session.NoteTextReplaced(string.Empty, "forward to");

        session.NoteTextReplaced("forward to", "at");

        Assert.Equal("at", session.CurrentWord);
        Assert.Equal(new[] { "looking" }, session.RecentWords);
    }

    [Fact]
    public void Undoing_a_completion_that_ended_in_a_space_restores_the_word_in_progress()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "i am looki");
        session.NoteTextReplaced("looki", "looking ");

        session.NoteTextReplaced("looking ", "looki");

        // "i" went when "looking" entered the two-word history, and history is deliberately never longer
        // than a trigram needs, so it cannot come back. What remains is still accurate.
        Assert.Equal("looki", session.CurrentWord);
        Assert.Equal(new[] { "am" }, session.RecentWords);
    }

    [Fact]
    public void Text_the_session_cannot_account_for_drops_the_context_rather_than_guessing()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "i am looking");

        session.NoteTextReplaced("something else", "x");

        Assert.Equal("x", session.CurrentWord);
        Assert.Empty(session.RecentWords);
    }

    [Fact]
    public void A_sentence_ending_insertion_starts_a_new_sentence()
    {
        using var session = NewSession();
        session.NoteTextReplaced(string.Empty, "i am looki");

        session.NoteTextReplaced("looki", "looking.");

        Assert.Equal(string.Empty, session.CurrentWord);
        Assert.Empty(session.RecentWords);
        Assert.True(session.IsAtSentenceStart);
    }
}
