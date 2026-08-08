using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;

namespace WordStrip.Core.Tests;

/// <summary>
/// Covers the persistent-bar state machine: what the strip shows between words, and what makes it go away.
///
/// <para>These drive the controller through its public surface only. The paths that begin with a real
/// keystroke — typing repopulating the bar after a dismissal, a committed word falling back to the idle
/// list — are not reachable from here: <c>TypingSession</c> raises its events from a low-level hook callback
/// that cannot be synthesised, and its character translation reads the live keyboard layout, so a test that
/// faked its way in would be asserting against whatever layout the build machine happens to use. Those paths
/// are verified end-to-end against Notepad instead, per the project's testing rules.</para>
/// </summary>
public class SuggestionControllerTests
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
    /// Everything one test needs, wired together. The hooks are constructed but never installed and never
    /// attached, so no system-wide hook exists and the typing buffer stays empty — which is exactly the
    /// "between words" state these tests are about.
    /// </summary>
    private sealed class Harness : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public FakeFocusProvider Focus { get; } = new();
        public FakeTextInjector Injector { get; } = new();
        public SuggestionController Controller { get; }
        public List<SuggestionUpdate> Published { get; } = new();

        private readonly LowLevelKeyboardHook _keyboardHook = new();
        private readonly LowLevelMouseHook _mouseHook = new();
        private readonly TypingSession _typingSession;

        public Harness(PredictionEngine engine)
        {
            _typingSession = new TypingSession(_keyboardHook, _mouseHook);
            Controller = new SuggestionController(
                _typingSession, engine, Injector, Settings, postToMessageLoop: null, focusProvider: Focus);

            Controller.SuggestionsChanged += (_, update) => Published.Add(update);
        }

        public SuggestionUpdate Last => Published[^1];

        public IReadOnlyList<string> LastWords => Last.Suggestions.Select(s => s.Word).ToList();

        public void Dispose()
        {
            Controller.Dispose();
            _typingSession.Dispose();
            _keyboardHook.Dispose();
            _mouseHook.Dispose();
        }
    }

    private readonly PredictionEngine _engine = TestVocabulary.BuildEngine();

    private Harness NewHarness() => new(_engine);

    // --- What the bar shows between words -----------------------------------------------------------

    [Fact]
    public void Accepting_a_word_leaves_the_common_words_on_screen_when_the_bar_is_persistent()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Settings.SuggestionCount = 4;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        // Descending frequency, ties broken ordinally — see TestVocabulary.
        Assert.Equal(new[] { "the", "and", "to", "a" }, h.LastWords);
    }

    [Fact]
    public void Common_words_are_tagged_as_such_so_the_ranker_can_tell_them_apart()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.All(h.Last.Suggestions, s => Assert.Equal(SuggestionSource.FrequentWord, s.Source));
    }

    [Fact]
    public void The_bar_shows_as_many_common_words_as_the_user_asked_for()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Settings.SuggestionCount = 7;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Equal(7, h.Last.Suggestions.Count);
    }

    [Fact]
    public void Accepting_a_word_clears_the_bar_when_the_bar_is_not_persistent()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = false;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void The_bar_is_updated_exactly_once_per_accepted_word()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Single(h.Published);
    }

    [Fact]
    public void A_paused_controller_shows_nothing_even_when_the_bar_is_persistent()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Controller.IsPaused = true;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Common_words_are_never_offered_in_a_password_field()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Focus.Focus = PasswordField;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Common_words_are_never_offered_outside_a_text_field()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Focus.Focus = NotATextField;

        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Empty(h.Last.Suggestions);
    }

    // --- Inserting a common word (nothing typed yet) -------------------------------------------------

    [Fact]
    public void Choosing_a_common_word_with_nothing_typed_inserts_it_followed_by_a_space()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Controller.AcceptSuggestion(new Suggestion("the", 1, 0));

        var replacement = Assert.Single(h.Injector.InProgressReplacements);
        Assert.Equal(string.Empty, replacement.Typed);
        Assert.Equal("the", replacement.Replacement);
        Assert.True(replacement.TrailingSpace);
    }

    [Fact]
    public void Nothing_is_injected_when_the_focused_control_is_not_a_text_field()
    {
        using var h = NewHarness();
        h.Focus.Focus = NotATextField;

        h.Controller.AcceptSuggestion(new Suggestion("the", 1, 0));

        Assert.Empty(h.Injector.InProgressReplacements);
    }

    [Fact]
    public void Nothing_is_injected_into_a_password_field()
    {
        using var h = NewHarness();
        h.Focus.Focus = PasswordField;

        h.Controller.AcceptSuggestion(new Suggestion("the", 1, 0));

        Assert.Empty(h.Injector.InProgressReplacements);
    }

    // --- Making the bar go away ----------------------------------------------------------------------

    [Fact]
    public void Dismissing_clears_the_bar()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Controller.Dismiss();

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void A_dismissed_bar_stays_away_even_though_it_is_persistent()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;

        h.Controller.Dismiss();
        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        // Without the dismissal being sticky, falling back to the idle list would put the bar straight back.
        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Focus_leaving_a_text_field_takes_a_visible_bar_away()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));
        Assert.NotEmpty(h.Last.Suggestions);

        h.Focus.Focus = NotATextField;
        h.Controller.PollFocus();

        Assert.Empty(h.Last.Suggestions);
    }

    [Fact]
    public void Polling_leaves_a_bar_alone_while_focus_is_still_in_a_text_field()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Controller.AcceptSuggestion(new Suggestion("world", 1, 0));
        var updatesSoFar = h.Published.Count;

        h.Controller.PollFocus();

        Assert.Equal(updatesSoFar, h.Published.Count);
    }

    [Fact]
    public void Polling_does_nothing_when_the_bar_is_already_hidden()
    {
        using var h = NewHarness();
        h.Settings.PersistentBar = true;
        h.Controller.Dismiss();
        var updatesSoFar = h.Published.Count;

        h.Focus.Focus = NotATextField;
        h.Controller.PollFocus();

        Assert.Equal(updatesSoFar, h.Published.Count);
    }
}
