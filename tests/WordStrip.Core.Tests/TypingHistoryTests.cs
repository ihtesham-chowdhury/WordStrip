using WordStrip.Core.Input;

namespace WordStrip.Core.Tests;

/// <summary>
/// The preceding-word history that feeds the language model.
///
/// <para>What matters here is not that words are remembered — it is that they are <em>forgotten</em> at
/// exactly the right moments. The history is a shadow of text the app cannot actually read, and a stale
/// entry does not degrade predictions politely: it produces confident, specific suggestions conditioned on
/// words that are no longer behind the cursor, which is indistinguishable from the model being broken.</para>
///
/// <para>These drive <see cref="TypingSession"/> through the public surface that does not require a
/// keyboard hook. The hook-driven paths — typing a word, pressing space — cannot be synthesised (see
/// SuggestionControllerTests for why) and are covered by the end-to-end regression instead.</para>
/// </summary>
public class TypingHistoryTests
{
    private static TypingSession NewSession() => new(new LowLevelKeyboardHook(), new LowLevelMouseHook());

    [Fact]
    public void A_new_session_has_no_history_and_reads_as_a_sentence_start()
    {
        using var session = NewSession();

        Assert.Empty(session.RecentWords);
        Assert.True(session.IsAtSentenceStart);
    }

    [Fact]
    public void An_inserted_suggestion_becomes_part_of_the_context()
    {
        using var session = NewSession();

        session.NoteWordInserted("hello");

        Assert.Equal(new[] { "hello" }, session.RecentWords);
        Assert.False(session.IsAtSentenceStart);
    }

    [Fact]
    public void Only_the_last_two_words_are_kept()
    {
        using var session = NewSession();

        session.NoteWordInserted("one");
        session.NoteWordInserted("two");
        session.NoteWordInserted("three");

        // Two is what a trigram consumes. Keeping more would be retaining the user's typing for no purpose.
        Assert.Equal(new[] { "two", "three" }, session.RecentWords);
    }

    [Fact]
    public void Clicking_elsewhere_discards_the_history()
    {
        using var session = NewSession();
        session.NoteWordInserted("hello");

        session.ResetBuffer();

        Assert.Empty(session.RecentWords);
        Assert.True(session.IsAtSentenceStart);
    }

    [Fact]
    public void The_history_is_discarded_even_when_there_was_no_word_in_progress()
    {
        // The common case for a click between words: the buffer is already empty, but the caret has still
        // moved somewhere unknown and the remembered words no longer describe what is behind it.
        using var session = NewSession();
        session.NoteWordInserted("hello");
        Assert.Empty(session.CurrentWord);

        session.ResetBuffer();

        Assert.Empty(session.RecentWords);
    }

    [Fact]
    public void Resetting_an_empty_buffer_still_does_not_raise_BufferReset()
    {
        // The bar's visibility keys off this event. Firing it more often than before would change when the
        // strip repaints, so clearing the history had to be added without disturbing it.
        using var session = NewSession();
        var raised = 0;
        session.BufferReset += (_, _) => raised++;

        session.ResetBuffer();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Autocorrect_rewrites_the_history_to_what_ended_up_on_screen()
    {
        using var session = NewSession();
        session.NoteWordInserted("teh");

        session.ReplaceLastWord("the");

        // Predicting from the typo rather than the correction would waste the one signal autocorrect just
        // produced, and condition the next prediction on a word the user cannot see.
        Assert.Equal(new[] { "the" }, session.RecentWords);
    }

    [Fact]
    public void Correcting_with_nothing_in_the_history_is_harmless()
    {
        using var session = NewSession();

        session.ReplaceLastWord("the");

        Assert.Empty(session.RecentWords);
    }

    [Fact]
    public void Blank_words_never_enter_the_history()
    {
        using var session = NewSession();

        session.NoteWordInserted("   ");
        session.NoteWordInserted(string.Empty);

        Assert.Empty(session.RecentWords);
    }
}
