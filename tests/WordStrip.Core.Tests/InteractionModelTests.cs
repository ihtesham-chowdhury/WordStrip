using WordStrip.Core.Personal;

namespace WordStrip.Core.Tests;

/// <summary>
/// The interaction model end to end, in memory: keys go through the same controller entry points the
/// router uses, and every assertion is on the text that actually ends up in the field.
///
/// <para>Two tasks, two behaviours. Part-way through a word, Space and closing punctuation finish it — but
/// only with a confident completion. Between words, one Tab inserts the first prediction, and Tab again
/// within the cycle window swaps it for the next. Everything else the user does ends that window, and the
/// swap only ever touches the exact text WordStrip inserted.</para>
/// </summary>
public class InteractionModelTests
{
    // --- Completion: Space and punctuation -----------------------------------------------------------

    [Fact]
    public void Space_finishes_a_word_with_its_one_confident_completion()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki ");

        Assert.Equal("i am looking ", h.Text);
    }

    [Fact]
    public void Space_completes_to_a_clear_favourite_among_several()
    {
        using var h = new InteractionHarness();

        h.Type("hel ");

        Assert.Equal("help ", h.Text);
    }

    [Fact]
    public void Space_stays_a_space_when_the_top_two_completions_are_nearly_tied()
    {
        using var h = new InteractionHarness();

        // "world" leads "work" by a couple of thousandths — nothing a Space should act on.
        h.Type("wor ");

        Assert.Equal("wor ", h.Text);
    }

    [Theory]
    [InlineData("look ")]
    [InlineData("can ")]
    [InlineData("work ")]
    public void Space_never_extends_a_word_that_is_already_complete(string typed)
    {
        using var h = new InteractionHarness();

        h.Type(typed);

        Assert.Equal(typed, h.Text);
    }

    [Theory]
    [InlineData(',', "i am looking,")]
    [InlineData('.', "i am looking.")]
    [InlineData('!', "i am looking!")]
    [InlineData('?', "i am looking?")]
    [InlineData(')', "i am looking)")]
    public void Closing_punctuation_commits_the_completion_and_keeps_the_punctuation(char key, string expected)
    {
        using var h = new InteractionHarness();

        h.Type("i am looki");
        h.Key(key);

        Assert.Equal(expected, h.Text);
    }

    [Fact]
    public void A_typo_that_happens_to_begin_a_rare_word_is_not_completed_into_it()
    {
        using var h = new InteractionHarness();

        // "tehran" is the only word "teh" begins — a confident completion by every other measure — but "the",
        // one transposition away, is thousands of times commoner. Found in real typing.
        h.Type("teh ");

        Assert.Equal("teh ", h.Text);
    }

    [Fact]
    public void A_completion_into_a_field_that_changed_invisibly_is_refused_and_the_key_still_arrives()
    {
        using var h = new InteractionHarness();

        h.Type("looki");
        h.Doc.ChangeInvisibly("xyz");
        h.Type(" ");

        Assert.Equal("xyz ", h.Text);
        Assert.Equal(1, h.Doc.Refusals);
    }

    [Fact]
    public void A_cycle_never_edits_a_field_that_changed_underneath_it()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Doc.ChangeInvisibly(string.Empty);
        h.Tab();

        Assert.Equal(string.Empty, h.Text);
        Assert.False(h.Last.IsActive);
    }

    [Fact]
    public void A_word_wordstrip_inserted_is_never_autocorrected_when_the_user_moves_on()
    {
        var personal = new PersonalVocabularyStore();
        personal.Add("Northfield Data Systms");  // "Systms" is one edit from nothing here, but stands in for "Halsted"
        using var h = new InteractionHarness(InteractionTestEngine.Build(personal));
        h.Settings.AutocorrectEnabled = true;

        h.Type("northf");
        h.Tab();
        h.Wait(h.Settings.PredictionCycleWindowMs + 100);
        h.Type(" ");

        Assert.Equal("Northfield Data Systms ", h.Text);
    }

    [Fact]
    public void Autocorrect_on_a_field_that_changed_invisibly_is_refused()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;

        h.Type("i am looking ");
        h.Tab();                              // leaves "for" as the word in progress
        h.Wait(h.Settings.PredictionCycleWindowMs + 100);
        h.Doc.ChangeInvisibly(string.Empty);  // the application clears its own field
        h.Type("i ");                         // WordStrip believes "fori" was just finished

        Assert.Equal("i ", h.Text);
    }

    // --- Written forms and capitals, when a word is finished ---------------------------------------------

    [Theory]
    [InlineData("im ", "I'm ")]
    [InlineData("i ", "I ")]
    [InlineData("ive ", "I've ")]
    [InlineData("dont ", "don't ")]
    [InlineData("london ", "London ")]
    [InlineData("i am in london, ", "I am in London, ")]
    public void A_finished_word_takes_its_written_form(string typed, string expected)
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;

        h.Type(typed);

        Assert.Equal(expected, h.Text);
    }

    [Theory]
    [InlineData("ill ")]
    [InlineData("were ")]
    [InlineData("well ")]
    [InlineData("don't ")]
    public void Ambiguous_or_already_written_words_are_left_as_typed(string typed)
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;

        h.Type(typed);

        Assert.Equal(typed, h.Text);
    }

    [Fact]
    public void The_first_word_of_a_sentence_is_capitalised_when_the_sentence_start_is_known()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;
        h.Doc.SentenceStartsAreKnown = true;

        h.Type("hello. how ");

        Assert.Equal("Hello. How ", h.Text);
    }

    [Fact]
    public void Capitals_and_apostrophes_are_fixed_even_with_spelling_autocorrect_off()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = false;
        h.Settings.FixCapitalsAndApostrophes = true;

        h.Type("im in london ");

        Assert.Equal("I'm in London ", h.Text);
    }

    [Fact]
    public void Capitals_and_apostrophes_can_be_switched_off()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = false;  // on its own, spelling correction turns "im" into "in"
        h.Settings.FixCapitalsAndApostrophes = false;

        h.Type("im in london ");

        Assert.Equal("im in london ", h.Text);
    }

    [Fact]
    public void With_both_on_im_becomes_I_m_rather_than_being_spell_corrected_to_in()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;

        h.Type("im ");

        Assert.Equal("I'm ", h.Text);
    }

    [Fact]
    public void Nothing_is_capitalised_on_a_guess()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;
        h.Doc.SentenceStartsAreKnown = false;  // the keyboard hook after a click: likely, but not known

        h.Type("hello ");

        Assert.Equal("hello ", h.Text);
    }

    [Fact]
    public void At_a_known_sentence_start_the_strip_shows_and_inserts_capitals()
    {
        using var h = new InteractionHarness();
        h.Settings.AutocorrectEnabled = true;
        h.Settings.FixCapitalsAndApostrophes = true;
        h.Doc.SentenceStartsAreKnown = true;

        h.Type("hello. looki");
        Assert.Equal("Looking", h.LastWords[0]);

        h.Type(" ");
        Assert.Equal("Hello. Looking ", h.Text);  // the start of the field is a known sentence start too
    }

    [Fact]
    public void Enter_never_carries_a_completion()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki");
        h.Enter();

        Assert.Equal("i am looki\n", h.Text);
    }

    [Fact]
    public void A_capitalised_prefix_keeps_its_capital()
    {
        using var h = new InteractionHarness();

        h.Type("Looki ");

        Assert.Equal("Looking ", h.Text);
    }

    [Fact]
    public void A_personal_word_completes_exactly_like_a_dictionary_word_and_keeps_its_own_casing()
    {
        var personal = new PersonalVocabularyStore();
        personal.Add("Northfield");
        using var h = new InteractionHarness(InteractionTestEngine.Build(personal));

        h.Type("northf ");

        Assert.Equal("Northfield ", h.Text);
    }

    [Fact]
    public void Backspace_after_a_completion_is_an_ordinary_backspace()
    {
        using var h = new InteractionHarness();

        h.Type("looki ");
        h.Backspace();

        Assert.Equal("looking", h.Text);
    }

    [Fact]
    public void Backspace_after_anything_else_is_an_ordinary_backspace()
    {
        using var h = new InteractionHarness();

        h.Type("looki x");
        h.Backspace();

        Assert.Equal("looking ", h.Text);
    }

    [Fact]
    public void A_dismissed_bar_completes_nothing()
    {
        using var h = new InteractionHarness();

        h.Type("looki");
        h.Escape();
        h.Type(" ");

        Assert.Equal("looki ", h.Text);
    }

    [Fact]
    public void Nothing_is_completed_in_a_password_field()
    {
        using var h = new InteractionHarness();
        h.Doc.IsPasswordField = true;

        h.Type("looki ");

        Assert.Equal("looki ", h.Text);
    }

    [Fact]
    public void When_the_provider_trails_the_keyboard_the_keystrokes_decide()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki");
        h.Doc.LagCharacters = 1;  // the text service still reports "look"
        h.Type(" ");

        Assert.Equal("i am looking ", h.Text);
    }

    [Fact]
    public void When_the_keystroke_record_lost_track_the_provider_decides()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki");
        h.KeySynchronousOverride = string.Empty;  // e.g. a click reset the keystroke record
        h.Type(" ");

        Assert.Equal("i am looking ", h.Text);
    }

    [Fact]
    public void A_quick_second_tab_swaps_the_prediction_even_while_the_first_is_still_being_reported()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Doc.LagCharacters = 2;  // the browser has only reported "i am looking f" so far
        h.Tab();

        Assert.Equal("i am looking at", h.Text);
    }

    [Fact]
    public void The_confidence_threshold_is_a_setting()
    {
        using var h = new InteractionHarness();
        h.Settings.CompletionMinConfidence = 0.95;

        h.Type("hel ");

        Assert.Equal("hel ", h.Text);
    }

    [Fact]
    public void Space_completion_can_be_switched_off()
    {
        using var h = new InteractionHarness();
        h.Settings.CompleteOnSpace = false;

        h.Type("looki ");

        Assert.Equal("looki ", h.Text);
    }

    [Fact]
    public void The_first_candidate_is_marked_armed_only_when_space_would_commit_it()
    {
        using var h = new InteractionHarness();

        h.Type("looki");
        Assert.True(h.Last.FirstIsArmed);

        h.Controller.Dismiss();
        h.Doc.Clear();
        h.Type("wor");
        Assert.False(h.Last.FirstIsArmed);
    }

    // --- Prediction: one Tab --------------------------------------------------------------------------

    [Fact]
    public void One_tab_between_words_inserts_the_first_prediction()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        Assert.True(h.Tab());

        Assert.Equal("i am looking for", h.Text);
    }

    [Fact]
    public void Tab_straight_after_a_finished_word_adds_the_space_and_the_prediction()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking");
        Assert.True(h.Tab());

        Assert.Equal("i am looking for", h.Text);
    }

    [Fact]
    public void Tab_on_a_part_typed_word_completes_it_without_a_space()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki");
        Assert.True(h.Tab());

        Assert.Equal("i am looking", h.Text);
    }

    [Fact]
    public void A_second_tab_within_the_window_replaces_the_prediction_with_the_next()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Wait(300);
        h.Tab();

        Assert.Equal("i am looking at", h.Text);
        Assert.Equal(("for", "at"), h.Doc.Replacements[^1]);
    }

    [Fact]
    public void Repeated_tabs_walk_the_candidates_and_wrap()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        var seen = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            h.Tab();
            seen.Add(h.Text["i am looking ".Length..]);
            h.Wait(200);
        }

        Assert.Equal(new[] { "for", "at", "back", "forward", "for" }, seen);
    }

    [Fact]
    public void Shift_tab_walks_backwards_during_a_cycle()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Tab(shift: true);

        Assert.Equal("i am looking forward", h.Text);
    }

    [Fact]
    public void Shift_tab_outside_a_cycle_is_left_to_the_application()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");

        Assert.False(h.Tab(shift: true));
    }

    [Fact]
    public void Tab_after_the_window_expires_inserts_a_new_prediction_instead_of_replacing()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Wait(h.Settings.PredictionCycleWindowMs + 100);
        h.Tab();

        Assert.Equal("i am looking for the", h.Text);
    }

    [Fact]
    public void The_bar_goes_back_to_passive_when_the_window_expires()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        Assert.True(h.Last.IsActive);

        h.Wait(h.Settings.PredictionCycleWindowMs + 100);

        Assert.False(h.Last.IsActive);
        Assert.Equal("the", h.LastWords[0]);  // now predicting what follows "for"
    }

    [Fact]
    public void Typing_after_a_prediction_ends_the_cycle_and_is_never_rewritten()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Type("ward");
        Assert.Equal("i am looking forward", h.Text);

        var replacementsBefore = h.Doc.Replacements.Count;
        h.Tab();

        // Tab now works from "forward" as typed; the earlier insertion is out of reach.
        Assert.StartsWith("i am looking forward", h.Text);
        Assert.DoesNotContain(h.Doc.Replacements.Skip(replacementsBefore), r => r.Existing.Length > 0);
    }

    [Fact]
    public void A_space_after_a_prediction_ends_the_cycle()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Type(" ");
        h.Tab();

        Assert.Equal("i am looking for the", h.Text);
    }

    [Fact]
    public void Moving_the_caret_ends_the_cycle()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Arrow();
        h.Tab();

        Assert.DoesNotContain(("for", "at"), h.Doc.Replacements);
    }

    [Fact]
    public void A_click_ends_the_cycle_and_tab_goes_back_to_the_application()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Click();

        Assert.False(h.Tab());
        Assert.Equal("i am looking for\t", h.Text);
    }

    [Fact]
    public void Focus_moving_elsewhere_ends_the_cycle()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Focus = 2;
        h.Tab();

        Assert.DoesNotContain(("for", "at"), h.Doc.Replacements);
    }

    [Fact]
    public void Esc_cancels_an_active_cycle_and_is_consumed_doing_so()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();

        Assert.True(h.Escape());
        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Esc_on_a_passive_bar_dismisses_it_but_still_reaches_the_application()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");

        Assert.False(h.Escape());
        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void After_esc_tab_belongs_to_the_application_until_the_user_types_again()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Escape();
        Assert.False(h.Tab());

        h.Type("x");
        h.Backspace();
        Assert.True(h.Tab());
    }

    [Fact]
    public void Tab_in_a_single_line_field_moves_to_the_next_field()
    {
        using var h = new InteractionHarness();
        h.Doc.IsSingleLine = true;

        h.Type("i am looking ");

        Assert.False(h.Tab());
    }

    [Fact]
    public void Tab_at_the_start_of_a_line_is_indentation()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking");
        h.Enter();

        Assert.False(h.Tab());
    }

    [Fact]
    public void A_multi_word_entry_is_inserted_and_replaced_as_one_span()
    {
        var personal = new PersonalVocabularyStore();
        personal.Add("Northfield Data Systems");
        personal.Add("Northfield");
        using var h = new InteractionHarness(InteractionTestEngine.Build(personal));

        h.Type("northf");
        h.Tab();
        var first = h.Text;
        h.Tab();

        Assert.Contains(h.Doc.Replacements, r => r.Existing == first);
        Assert.NotEqual(first, h.Text);
        Assert.True(h.Doc.ShadowMatchesText);
    }

    [Fact]
    public void The_provider_and_the_field_agree_after_every_kind_of_insertion()
    {
        using var h = new InteractionHarness();

        h.Type("i am looki ");
        h.Tab();
        h.Tab();
        h.Tab();
        h.Type(", hel.");

        Assert.True(h.Doc.ShadowMatchesText, $"field '{h.Text}'");
        Assert.Equal(0, h.Doc.Refusals);
    }

    // --- Passive and active ---------------------------------------------------------------------------

    [Fact]
    public void Typing_never_makes_the_bar_active()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking forward to it");

        Assert.All(h.Published, u => Assert.False(u.IsActive));
    }

    [Fact]
    public void Each_tab_marks_the_candidate_it_inserted()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        Assert.Equal(0, h.Last.SelectedIndex);
        Assert.Equal("for", h.LastWords[0]);

        h.Tab();
        Assert.Equal(1, h.Last.SelectedIndex);
    }

    [Fact]
    public void The_bar_only_shows_what_it_has_room_for()
    {
        using var h = new InteractionHarness();
        h.Settings.SuggestionCount = 7;
        h.Controller.VisibleSlotLimit = 3;

        h.Type("i am looking ");

        Assert.Equal(3, h.Last.Suggestions.Count);
    }

    // --- The mouse still works ------------------------------------------------------------------------

    [Fact]
    public void Clicking_an_alternative_during_a_cycle_replaces_the_inserted_word()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Tab();
        h.Choose(h.Last.Suggestions[2]);

        Assert.Equal("i am looking back ", h.Text);
    }

    [Fact]
    public void Clicking_a_prediction_between_words_inserts_it_with_a_space()
    {
        using var h = new InteractionHarness();

        h.Type("i am looking ");
        h.Choose(h.Last.Suggestions[1]);

        Assert.Equal("i am looking at ", h.Text);
    }
}
