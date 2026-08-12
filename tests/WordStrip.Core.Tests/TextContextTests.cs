using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;
using WordStrip.Core.Text;

namespace WordStrip.Core.Tests;

/// <summary>
/// Covers the Phase 7 text-context abstraction: the value type the prediction layer consumes, the adapter
/// over the existing keyboard-hook path, and the controller behaviour that a substitutable provider finally
/// makes reachable.
///
/// <para>That last part is the point of the phase, and it is worth being explicit about. Until now
/// <c>SuggestionControllerTests</c> carried a comment saying the paths beginning with a real keystroke could
/// not be tested — <c>TypingSession</c> raises its events from a low-level hook callback that cannot be
/// synthesised, and its character translation reads the live keyboard layout, so a faked test would assert
/// against whatever layout the build machine happened to use. Those paths are covered here, by a fake
/// provider that announces words the way any provider does. The abstraction was introduced for TSF; making
/// half the controller testable is the part that pays for itself immediately.</para>
/// </summary>
public class TextContextTests
{
    private static readonly FocusedControlInfo TextField = new(IsStandardEditControl: true, IsPasswordField: false);
    private static readonly FocusedControlInfo PasswordField = new(IsStandardEditControl: true, IsPasswordField: true);
    private static readonly FocusedControlInfo NotATextField = default;

    private sealed class FakeFocusProvider : IFocusedControlProvider
    {
        public FocusedControlInfo Focus { get; set; } = TextField;
        public FocusedControlInfo GetFocusedControlInfo() => Focus;
    }

    private sealed class FakeTextInjector : ITextInjector
    {
        public List<(string Typed, string Replacement, bool TrailingSpace)> InProgressReplacements { get; } = new();
        public List<(string Typed, char Boundary, string Replacement)> CommittedReplacements { get; } = new();

        public void ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace) =>
            InProgressReplacements.Add((typedWord, replacement, appendTrailingSpace));

        public void ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement) =>
            CommittedReplacements.Add((typedWord, boundaryChar, replacement));
    }

    /// <summary>
    /// A provider driven by the test rather than by a keyboard. Reports itself as <see cref="TextContextSource.TextServices"/>
    /// so anything that accidentally special-cased the hook would show up as a failure here.
    /// </summary>
    private sealed class FakeContextProvider : ITextContextProvider
    {
        private readonly List<string> _precedingWords = new();

        public bool IsEditable { get; set; } = true;
        public bool IsPasswordField { get; set; }
        public bool IsAtSentenceStart { get; set; } = true;
        public CaretRect? Caret { get; set; }
        public bool IsAvailable { get; set; } = true;
        public TextContextSource Source => TextContextSource.TextServices;

        public string CurrentWord { get; private set; } = string.Empty;
        public List<string> Inserted { get; } = new();
        public List<string> Corrections { get; } = new();
        public bool WasDisposed { get; private set; }

        public event EventHandler<string>? CurrentWordChanged;
        public event EventHandler<WordCommittedEventArgs>? WordCommitted;
        public event EventHandler? ContextLost;

        public TextContext GetContext() => new(
            IsEditable, IsPasswordField, CurrentWord, _precedingWords.ToArray(),
            IsAtSentenceStart, Caret, Source);

        public void NoteTextInserted(string text)
        {
            Inserted.Add(text);

            var hadWord = CurrentWord.Length > 0;
            CurrentWord = string.Empty;
            _precedingWords.Add(text);
            IsAtSentenceStart = false;

            if (hadWord) ContextLost?.Invoke(this, EventArgs.Empty);
        }

        public void NoteWordCorrected(string correctedWord)
        {
            Corrections.Add(correctedWord);
            if (_precedingWords.Count > 0) _precedingWords[^1] = correctedWord;
        }

        public void Dispose() => WasDisposed = true;

        // --- drivers, standing in for keystrokes -----------------------------------------------------

        public void Type(string word)
        {
            CurrentWord = word;
            CurrentWordChanged?.Invoke(this, word);
        }

        public void Commit(string word, char boundary = ' ')
        {
            var precedingBefore = _precedingWords.ToArray();

            CurrentWord = string.Empty;
            _precedingWords.Add(word);
            IsAtSentenceStart = false;

            WordCommitted?.Invoke(this, new WordCommittedEventArgs
            {
                Word = word,
                BoundaryChar = boundary,
                PrecedingWords = precedingBefore,
            });

            CurrentWordChanged?.Invoke(this, string.Empty);
        }

        public void LoseContext()
        {
            CurrentWord = string.Empty;
            _precedingWords.Clear();
            IsAtSentenceStart = true;
            ContextLost?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class Harness : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public FakeContextProvider Context { get; } = new();
        public FakeTextInjector Injector { get; } = new();
        public SuggestionController Controller { get; }
        public List<SuggestionUpdate> Published { get; } = new();

        public Harness(PredictionEngine engine, PersonalLanguageModel? learning = null)
        {
            Controller = new SuggestionController(
                Context, engine, Injector, Settings, postToMessageLoop: null, personalLearning: learning);

            Controller.SuggestionsChanged += (_, update) => Published.Add(update);
        }

        public SuggestionUpdate Last => Published[^1];

        public IReadOnlyList<string> LastWords => Last.Suggestions.Select(s => s.Word).ToList();

        public void Dispose() => Controller.Dispose();
    }

    private readonly PredictionEngine _engine = TestVocabulary.BuildEngine();

    private Harness NewHarness(PersonalLanguageModel? learning = null) => new(_engine, learning);

    private static PersonalLanguageModel NewLearningModel() =>
        new(Path.Combine(Path.GetTempPath(), "wordstrip-tests", Guid.NewGuid().ToString("N") + ".json"));

    // --- The context value ----------------------------------------------------------------------------

    [Fact]
    public void An_ordinary_text_field_is_suggestible() =>
        Assert.True(new TextContext(true, false, "", Array.Empty<string>(), true, null, TextContextSource.KeyboardHook)
            .IsSuggestible);

    [Fact]
    public void A_password_field_is_never_suggestible() =>
        Assert.False(new TextContext(true, true, "", Array.Empty<string>(), true, null, TextContextSource.KeyboardHook)
            .IsSuggestible);

    [Fact]
    public void Something_that_is_not_editable_is_never_suggestible() =>
        Assert.False(new TextContext(false, false, "", Array.Empty<string>(), true, null, TextContextSource.KeyboardHook)
            .IsSuggestible);

    [Fact]
    public void The_empty_context_is_not_suggestible() => Assert.False(TextContext.None.IsSuggestible);

    [Fact]
    public void The_empty_context_reads_as_a_sentence_start()
    {
        // With nothing known about the surroundings, the model should answer as if a sentence were beginning
        // rather than conditioning on words it cannot see.
        Assert.True(TextContext.None.IsAtSentenceStart);
        Assert.Empty(TextContext.None.PrecedingWords);
    }

    // --- The keyboard-hook provider -------------------------------------------------------------------

    private sealed class HookHarness : IDisposable
    {
        private readonly LowLevelKeyboardHook _keyboardHook = new();
        private readonly LowLevelMouseHook _mouseHook = new();

        public TypingSession Session { get; }
        public FakeFocusProvider Focus { get; } = new();
        public KeyboardHookTextContextProvider Provider { get; }

        public HookHarness()
        {
            // Constructed but never installed and never attached, so no system-wide hook exists.
            Session = new TypingSession(_keyboardHook, _mouseHook);
            Provider = new KeyboardHookTextContextProvider(Session, Focus);
        }

        public void Dispose()
        {
            Provider.Dispose();
            Session.Dispose();
            _keyboardHook.Dispose();
            _mouseHook.Dispose();
        }
    }

    [Fact]
    public void The_hook_provider_identifies_itself_as_the_hook_path()
    {
        using var h = new HookHarness();
        Assert.Equal(TextContextSource.KeyboardHook, h.Provider.Source);
    }

    [Fact]
    public void The_hook_provider_stays_available_even_where_it_cannot_suggest()
    {
        using var h = new HookHarness();
        h.Focus.Focus = NotATextField;

        // Availability is about whether the mechanism works at all, not about the control under the caret.
        // Conflating them would make the fallback look like it had dropped out every time the user clicked a
        // button, and a TSF provider would then never hand back.
        Assert.True(h.Provider.IsAvailable);
        Assert.False(h.Provider.GetContext().IsSuggestible);
    }

    [Fact]
    public void The_hook_provider_reports_a_password_field_as_such()
    {
        using var h = new HookHarness();
        h.Focus.Focus = PasswordField;

        var context = h.Provider.GetContext();

        Assert.True(context.IsEditable);
        Assert.True(context.IsPasswordField);
        Assert.False(context.IsSuggestible);
    }

    [Fact]
    public void The_hook_provider_passes_the_caret_through()
    {
        using var h = new HookHarness();
        h.Focus.Focus = new FocusedControlInfo(true, false, new CaretRect(10, 20, 12, 40));

        Assert.Equal(new CaretRect(10, 20, 12, 40), h.Provider.GetContext().Caret);
    }

    [Fact]
    public void The_hook_provider_never_claims_to_know_about_a_selection()
    {
        using var h = new HookHarness();

        // A keystroke observer cannot see a mouse selection. Answering "false" is a statement that nothing is
        // known, not a reading — which is exactly the sort of thing TSF is supposed to improve on.
        Assert.False(h.Provider.GetContext().HasSelection);
    }

    [Fact]
    public void An_inserted_word_becomes_part_of_the_context()
    {
        using var h = new HookHarness();

        h.Provider.NoteTextInserted("hello");

        var context = h.Provider.GetContext();
        Assert.Equal(new[] { "hello" }, context.PrecedingWords);
        Assert.False(context.IsAtSentenceStart);
    }

    [Fact]
    public void A_correction_rewrites_the_last_word_of_the_context()
    {
        using var h = new HookHarness();
        h.Provider.NoteTextInserted("recieve");

        h.Provider.NoteWordCorrected("receive");

        Assert.Equal(new[] { "receive" }, h.Provider.GetContext().PrecedingWords);
    }

    [Fact]
    public void Disposing_the_hook_provider_leaves_the_typing_session_working()
    {
        using var h = new HookHarness();

        h.Provider.Dispose();

        // The composition root owns the session, because its hook subscription order relative to the bar's
        // input router is load-bearing. A provider that disposed it would take the whole input path down.
        h.Session.NoteWordInserted("world");
        Assert.Equal(new[] { "world" }, h.Session.RecentWords);
    }

    [Fact]
    public void A_disposed_hook_provider_stops_forwarding_events()
    {
        using var h = new HookHarness();
        var seen = 0;
        h.Provider.ContextLost += (_, _) => seen++;

        h.Provider.Dispose();
        h.Session.NoteWordInserted("world");

        Assert.Equal(0, seen);
    }

    // --- Controller paths that a substitutable provider finally makes reachable -----------------------

    [Fact]
    public void Typing_a_word_publishes_completions_for_it()
    {
        using var h = NewHarness();

        h.Context.Type("wor");

        Assert.Equal(new[] { "world", "work", "working", "word" }, h.LastWords);
        Assert.False(h.Last.IsIdle);
    }

    [Fact]
    public void Typing_brings_back_a_bar_the_user_dismissed()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Controller.Dismiss();
        Assert.Empty(h.Last.Suggestions);

        h.Context.Type("wor");

        // Dismissal is sticky against every path that republishes on its own, but typing is the signal that
        // the user wants it back. Getting this wrong strands the bar until a restart.
        Assert.NotEmpty(h.Last.Suggestions);
    }

    [Fact]
    public void Finishing_a_word_falls_back_to_the_between_words_list()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Context.Commit("hello");

        Assert.True(h.Last.IsIdle);
        Assert.NotEmpty(h.Last.Suggestions);
    }

    [Fact]
    public void Finishing_a_word_clears_the_bar_when_it_is_not_persistent()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = false;

        h.Context.Commit("hello");

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Nothing_is_suggested_while_typing_in_a_password_field()
    {
        using var h = NewHarness();
        h.Context.IsPasswordField = true;

        h.Context.Type("wor");

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Nothing_is_suggested_while_typing_outside_a_text_field()
    {
        using var h = NewHarness();
        h.Context.IsEditable = false;

        h.Context.Type("wor");

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void A_paused_controller_suggests_nothing_while_typing()
    {
        using var h = NewHarness();
        h.Controller.IsPaused = true;

        h.Context.Type("wor");

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Losing_the_context_falls_back_to_the_between_words_list()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Context.LoseContext();

        Assert.True(h.Last.IsIdle);
    }

    [Fact]
    public void The_caret_from_the_provider_reaches_the_bar()
    {
        using var h = NewHarness();
        h.Context.Caret = new CaretRect(4, 8, 6, 24);

        h.Context.Type("wor");

        Assert.Equal(new CaretRect(4, 8, 6, 24), h.Last.Caret);
    }

    // --- Autocorrect and learning, through the provider -----------------------------------------------

    [Fact]
    public void A_misspelling_is_corrected_when_the_word_is_finished()
    {
        using var h = NewHarness();

        h.Context.Commit("recieve");

        var correction = Assert.Single(h.Injector.CommittedReplacements);
        Assert.Equal("recieve", correction.Typed);
        Assert.Equal("receive", correction.Replacement);
        Assert.Equal(' ', correction.Boundary);
    }

    [Fact]
    public void The_provider_is_told_about_a_correction_so_context_follows_the_screen()
    {
        using var h = NewHarness();

        h.Context.Commit("recieve");

        // Without this the next prediction would be conditioned on the typo the app just removed.
        Assert.Equal(new[] { "receive" }, h.Context.Corrections);
    }

    [Fact]
    public void Nothing_is_corrected_in_a_password_field()
    {
        using var h = NewHarness();
        h.Context.IsPasswordField = true;

        h.Context.Commit("recieve");

        Assert.Empty(h.Injector.CommittedReplacements);
    }

    [Fact]
    public void Nothing_is_corrected_when_autocorrect_is_switched_off()
    {
        using var h = NewHarness();
        h.Settings.AutocorrectEnabled = false;

        h.Context.Commit("recieve");

        Assert.Empty(h.Injector.CommittedReplacements);
    }

    [Fact]
    public void A_finished_word_is_learned_when_learning_is_on()
    {
        var learning = NewLearningModel();
        using var h = NewHarness(learning);
        h.Settings.PersonalLearningEnabled = true;

        h.Context.Commit("hello");

        Assert.Equal(1, learning.GetUnigramCount("hello"));
    }

    [Fact]
    public void Nothing_is_learned_when_learning_is_off()
    {
        var learning = NewLearningModel();
        using var h = NewHarness(learning);
        h.Settings.PersonalLearningEnabled = false;

        h.Context.Commit("hello");

        Assert.Equal(0, learning.GetUnigramCount("hello"));
    }

    [Fact]
    public void Nothing_is_learned_from_a_password_field()
    {
        var learning = NewLearningModel();
        using var h = NewHarness(learning);
        h.Settings.PersonalLearningEnabled = true;
        h.Context.IsPasswordField = true;

        h.Context.Commit("hello");

        // The same predicate gates suggesting and learning. A field we would not offer suggestions in is one
        // we must not record from either.
        Assert.Equal(0, learning.GetUnigramCount("hello"));
    }

    [Fact]
    public void Nothing_is_learned_from_a_surface_we_could_not_identify()
    {
        var learning = NewLearningModel();
        using var h = NewHarness(learning);
        h.Settings.PersonalLearningEnabled = true;
        h.Context.IsEditable = false;

        h.Context.Commit("hello");

        Assert.Equal(0, learning.GetUnigramCount("hello"));
    }

    [Fact]
    public void The_corrected_word_is_learned_rather_than_the_typo()
    {
        var learning = NewLearningModel();
        using var h = NewHarness(learning);
        h.Settings.PersonalLearningEnabled = true;

        h.Context.Commit("recieve");

        Assert.Equal(1, learning.GetUnigramCount("receive"));
        Assert.Equal(0, learning.GetUnigramCount("recieve"));
    }

    // --- Ownership ------------------------------------------------------------------------------------

    [Fact]
    public void The_controller_does_not_dispose_a_provider_it_was_given()
    {
        var h = NewHarness();

        h.Controller.Dispose();

        // The caller that built the provider may still be using it — for instance to decide whether TSF is
        // available for the next application the user switches to.
        Assert.False(h.Context.WasDisposed);
    }

    [Fact]
    public void The_controller_reports_which_mechanism_is_feeding_it()
    {
        using var h = NewHarness();
        Assert.Equal(TextContextSource.TextServices, h.Controller.ContextSource);
    }
}
