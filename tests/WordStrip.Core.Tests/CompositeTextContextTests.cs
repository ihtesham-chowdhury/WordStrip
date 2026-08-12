using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Suggestions;
using WordStrip.Core.Text;

namespace WordStrip.Core.Tests;

/// <summary>
/// Covers the Phase 7 fallback machinery: which input mechanism answers, what happens when the preferred one
/// is unavailable, and what happens when it is outright broken.
///
/// <para>The brief states the requirement three times in three different sections — usable where TSF is
/// unavailable, never fail wholesale because one mechanism is unsupported, and never prevent normal typing
/// because prediction errored. These are the tests for that, and they are written before the provider they
/// are meant to protect against exists, which is the only order in which a fallback gets taken
/// seriously.</para>
/// </summary>
public class CompositeTextContextTests
{
    /// <summary>
    /// A provider whose availability, answers and failure modes are all set by the test. Every operation can
    /// be made to throw, because "a provider that misbehaves" is the entire subject here.
    /// </summary>
    private sealed class ControllableProvider : ITextContextProvider
    {
        private readonly List<string> _precedingWords = new();

        public ControllableProvider(TextContextSource source) => Source = source;

        public TextContextSource Source { get; }

        public bool Available { get; set; } = true;
        public bool ThrowOnIsAvailable { get; set; }
        public bool ThrowOnGetContext { get; set; }
        public bool ThrowOnNote { get; set; }

        public string CurrentWord { get; set; } = string.Empty;
        public int GetContextCalls { get; private set; }
        public List<string> Inserted { get; } = new();
        public List<string> Corrections { get; } = new();
        public bool WasDisposed { get; private set; }

        public bool IsAvailable
        {
            get
            {
                if (ThrowOnIsAvailable) throw new InvalidOperationException("availability check exploded");
                return Available;
            }
        }

        public event EventHandler<string>? CurrentWordChanged;
        public event EventHandler<WordCommittedEventArgs>? WordCommitted;
        public event EventHandler? ContextLost;

        public TextContext GetContext()
        {
            GetContextCalls++;
            if (ThrowOnGetContext) throw new InvalidOperationException("context read exploded");

            return new TextContext(
                IsEditable: true,
                IsPasswordField: false,
                CurrentWord: CurrentWord,
                PrecedingWords: _precedingWords.ToArray(),
                IsAtSentenceStart: _precedingWords.Count == 0,
                Caret: null,
                Source: Source);
        }

        public void NoteTextInserted(string text)
        {
            if (ThrowOnNote) throw new InvalidOperationException("note exploded");
            Inserted.Add(text);
            _precedingWords.Add(text);
            CurrentWord = string.Empty;
        }

        public void NoteWordCorrected(string correctedWord)
        {
            if (ThrowOnNote) throw new InvalidOperationException("note exploded");
            Corrections.Add(correctedWord);
            if (_precedingWords.Count > 0) _precedingWords[^1] = correctedWord;
        }

        public void Dispose() => WasDisposed = true;

        // --- drivers ---------------------------------------------------------------------------------

        public void RaiseWord(string word)
        {
            CurrentWord = word;
            CurrentWordChanged?.Invoke(this, word);
        }

        public void RaiseCommit(string word) => WordCommitted?.Invoke(this, new WordCommittedEventArgs
        {
            Word = word,
            BoundaryChar = ' ',
            PrecedingWords = Array.Empty<string>(),
        });

        public void RaiseContextLost() => ContextLost?.Invoke(this, EventArgs.Empty);
    }

    private static ControllableProvider Rich() => new(TextContextSource.TextServices);

    private static ControllableProvider Fallback() => new(TextContextSource.KeyboardHook);

    // --- Choosing a provider --------------------------------------------------------------------------

    [Fact]
    public void The_preferred_provider_answers_when_it_is_available()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.TextServices, composite.GetContext().Source);
        Assert.Equal(0, hook.GetContextCalls);
    }

    [Fact]
    public void The_fallback_answers_when_the_preferred_provider_is_unavailable()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.KeyboardHook, composite.GetContext().Source);
    }

    [Fact]
    public void Availability_is_reconsidered_on_every_call()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.KeyboardHook, composite.GetContext().Source);

        // TSF availability follows the focused application, and alt-tabbing raises no event. Caching the
        // decision would strand the user on the fallback for the rest of the session.
        rich.Available = true;
        Assert.Equal(TextContextSource.TextServices, composite.GetContext().Source);
    }

    [Fact]
    public void With_no_provider_available_the_answer_is_that_nothing_is_known()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        hook.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        var context = composite.GetContext();

        // Not an exception and not a crash: the same "nothing here" the stack already handles for a button.
        Assert.False(context.IsSuggestible);
        Assert.Equal(string.Empty, context.CurrentWord);
    }

    [Fact]
    public void A_composite_needs_at_least_one_provider() =>
        Assert.Throws<ArgumentException>(() => new CompositeTextContextProvider());

    // --- A broken provider must not break typing -------------------------------------------------------

    [Fact]
    public void A_provider_that_throws_while_reading_context_is_replaced_by_the_fallback()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnGetContext = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.KeyboardHook, composite.GetContext().Source);
    }

    [Fact]
    public void A_provider_that_throws_is_not_asked_again()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnGetContext = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        composite.GetContext();
        var callsAfterFailure = rich.GetContextCalls;
        composite.GetContext();
        composite.GetContext();

        // Retrying every keystroke would pay for the same exception dozens of times a second while someone
        // is typing. Demotion is permanent for the process — see the comment on the field.
        Assert.Equal(callsAfterFailure, rich.GetContextCalls);
    }

    [Fact]
    public void A_provider_that_throws_from_its_availability_check_is_also_taken_out_of_service()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnIsAvailable = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.KeyboardHook, composite.GetContext().Source);
    }

    [Fact]
    public void Every_provider_failing_still_does_not_throw()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnGetContext = true;
        hook.ThrowOnGetContext = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        var context = composite.GetContext();

        Assert.False(context.IsSuggestible);
        Assert.False(composite.IsAvailable);
    }

    [Fact]
    public void A_provider_that_throws_while_being_told_about_an_insertion_does_not_stop_the_others()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnNote = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        composite.NoteTextInserted("hello");

        Assert.Equal(new[] { "hello" }, hook.Inserted);
    }

    // --- Keeping dormant providers correct --------------------------------------------------------------

    [Fact]
    public void An_insertion_is_reported_to_every_provider_not_just_the_active_one()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        composite.NoteTextInserted("hello");

        // The hook filters injected keystrokes out of its shadow buffer, so a word accepted while TSF was
        // driving would be invisible to it. Switching back mid-sentence would then lose that word.
        Assert.Equal(new[] { "hello" }, rich.Inserted);
        Assert.Equal(new[] { "hello" }, hook.Inserted);
    }

    [Fact]
    public void A_correction_is_reported_to_every_provider()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        composite.NoteTextInserted("recieve");
        composite.NoteWordCorrected("receive");

        Assert.Equal(new[] { "receive" }, rich.Corrections);
        Assert.Equal(new[] { "receive" }, hook.Corrections);
    }

    // --- Events come from one provider at a time --------------------------------------------------------

    [Fact]
    public void Only_the_active_provider_announces_words()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        var announced = new List<string>();
        composite.CurrentWordChanged += (_, word) => announced.Add(word);

        // Both providers are watching the same typing. Without filtering, the bar would be published twice
        // for every keystroke.
        rich.RaiseWord("wor");
        hook.RaiseWord("wor");

        Assert.Equal(new[] { "wor" }, announced);
    }

    [Fact]
    public void The_fallback_announces_words_once_the_preferred_provider_steps_aside()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        var announced = new List<string>();
        composite.CurrentWordChanged += (_, word) => announced.Add(word);

        hook.RaiseWord("wor");

        Assert.Equal(new[] { "wor" }, announced);
    }

    [Fact]
    public void A_demoted_provider_stops_being_listened_to()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnGetContext = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        composite.GetContext(); // demotes rich

        var announced = new List<string>();
        composite.CurrentWordChanged += (_, word) => announced.Add(word);
        rich.RaiseWord("ignored");

        Assert.Empty(announced);
    }

    [Fact]
    public void Commits_and_context_loss_are_filtered_the_same_way()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        var commits = 0;
        var losses = 0;
        composite.WordCommitted += (_, _) => commits++;
        composite.ContextLost += (_, _) => losses++;

        rich.RaiseCommit("hello");
        hook.RaiseCommit("hello");
        rich.RaiseContextLost();
        hook.RaiseContextLost();

        Assert.Equal(1, commits);
        Assert.Equal(1, losses);
    }

    // --- Diagnostics and ownership ----------------------------------------------------------------------

    [Fact]
    public void The_composite_reports_which_mechanism_is_in_use()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        Assert.Equal(TextContextSource.KeyboardHook, composite.Source);
    }

    [Fact]
    public void Switching_mechanism_raises_an_event_so_it_can_be_logged()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.Available = false;
        using var composite = new CompositeTextContextProvider(rich, hook);

        var switches = new List<TextContextSource>();
        composite.ActiveSourceChanged += (_, source) => switches.Add(source);

        composite.GetContext();
        composite.GetContext();  // same provider, should not re-announce
        rich.Available = true;
        composite.GetContext();

        Assert.Equal(new[] { TextContextSource.KeyboardHook, TextContextSource.TextServices }, switches);
    }

    [Fact]
    public void Disposing_the_composite_leaves_its_providers_alive()
    {
        var rich = Rich();
        var hook = Fallback();
        var composite = new CompositeTextContextProvider(rich, hook);

        composite.Dispose();

        // Whoever built them owns them, same rule the controller and the hook provider follow.
        Assert.False(rich.WasDisposed);
        Assert.False(hook.WasDisposed);
    }

    [Fact]
    public void A_disposed_composite_stops_forwarding()
    {
        var rich = Rich();
        var hook = Fallback();
        var composite = new CompositeTextContextProvider(rich, hook);

        var announced = 0;
        composite.CurrentWordChanged += (_, _) => announced++;
        composite.Dispose();
        rich.RaiseWord("wor");

        Assert.Equal(0, announced);
    }

    // --- End to end: the controller keeps working through a provider failure ---------------------------

    private sealed class FakeTextInjector : ITextInjector
    {
        public List<string> Replacements { get; } = new();

        public void ReplaceInProgressWord(string typedWord, string replacement, bool appendTrailingSpace) =>
            Replacements.Add(replacement);

        public void ReplaceCommittedWord(string typedWord, char boundaryChar, string replacement) =>
            Replacements.Add(replacement);
    }

    [Fact]
    public void Suggestions_keep_working_after_the_preferred_provider_dies_mid_sentence()
    {
        var rich = Rich();
        var hook = Fallback();
        using var composite = new CompositeTextContextProvider(rich, hook);

        var settings = new AppSettings();
        var injector = new FakeTextInjector();
        var published = new List<SuggestionUpdate>();

        using var controller = new SuggestionController(
            composite, TestVocabulary.BuildEngine(), injector, settings, postToMessageLoop: null);
        controller.SuggestionsChanged += (_, update) => published.Add(update);

        rich.RaiseWord("wor");
        Assert.NotEmpty(published[^1].Suggestions);

        // The rich provider breaks. Typing must carry on through the fallback with no gap.
        rich.ThrowOnGetContext = true;
        rich.ThrowOnIsAvailable = true;
        hook.RaiseWord("wor");

        Assert.NotEmpty(published[^1].Suggestions);
        Assert.Equal(TextContextSource.KeyboardHook, controller.ContextSource);
    }

    [Fact]
    public void Accepting_a_suggestion_still_reaches_the_document_when_the_preferred_provider_is_broken()
    {
        var rich = Rich();
        var hook = Fallback();
        rich.ThrowOnGetContext = true;
        using var composite = new CompositeTextContextProvider(rich, hook);

        var settings = new AppSettings();
        var injector = new FakeTextInjector();

        using var controller = new SuggestionController(
            composite, TestVocabulary.BuildEngine(), injector, settings, postToMessageLoop: null);

        controller.AcceptSuggestion(new Suggestion("world", 1, 0));

        Assert.Equal(new[] { "world" }, injector.Replacements);
    }
}
