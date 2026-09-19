using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;
using WordStrip.Core.Text;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// Ties everything together: watches an <see cref="ITextContextProvider"/> for the word currently being
/// typed and what surrounds it, asks <see cref="PredictionEngine"/> for candidates, owns what the keyboard
/// does with them, and performs replacements via <see cref="ITextInjector"/>. This is the only class the UI
/// layer needs to talk to — it never touches hooks or the prediction engine directly. Reads every setting
/// live from the shared <see cref="AppSettings"/>, so changes take effect on the next keystroke.
///
/// <para><b>Two tasks, two behaviours.</b> While a word is part-typed the question is "which word is this?"
/// and the answer is taken the way typing is finished anyway: Space or closing punctuation commits a
/// completion, but only a confident one (<see cref="CompletionPolicy"/>). Between words the question is
/// "what comes next?" and a single Tab inserts the first prediction outright — no highlight step, no second
/// key. Tab again within <see cref="AppSettings.PredictionCycleWindowMs"/> swaps what was just inserted for
/// the next candidate. That swap is a real span: WordStrip records exactly the text it put before the caret
/// and replaces exactly that, and anything else the user does in between — a letter, a click, an arrow,
/// focus moving — ends it.</para>
///
/// <para><b>Passive until asked.</b> Model updates only ever change what the bar says. The bar becomes active
/// (a visible selection) solely because the user pressed Tab, and returns to passive when the cycle ends.</para>
///
/// <para>Every text change is deferred through <c>postToMessageLoop</c>: the key handlers here are called
/// from inside the low-level keyboard hook, where injected input is either discarded or interleaved with the
/// key still in flight. The provider is told about the change immediately, so the next keystroke is judged
/// against the text as it is about to be.</para>
/// </summary>
public sealed class SuggestionController : IDisposable
{
    private readonly ITextContextProvider _context;
    private readonly PredictionEngine _predictionEngine;
    private readonly ITextInjector _textInjector;
    private readonly AppSettings _settings;
    private readonly Action<Action> _postToMessageLoop;
    private readonly PersonalLanguageModel? _personalLearning;
    private readonly Prediction.Neural.NeuralRerankCoordinator? _neuralReranking;
    private readonly TimeProvider _time;
    private readonly Func<nint> _focusIdentity;
    private readonly Func<string>? _keySynchronousWord;
    private readonly Action<TimeSpan, Action>? _scheduleAfter;

    /// <summary>Whether this instance created the context provider and must therefore dispose it.</summary>
    private readonly bool _ownsContextProvider;

    /// <summary>
    /// Set when the user explicitly took the bar away (Esc, or a click outside it) and cleared as soon as
    /// they start typing a word again. Without it, a dismissed bar would immediately come back: dismissing
    /// is usually accompanied by a context loss, and that itself would republish the idle list.
    /// </summary>
    private bool _dismissed;

    /// <summary>Whether the last thing published was non-empty, i.e. the bar is currently showing something.</summary>
    private bool _isShowing;

    /// <summary>Set while <see cref="AcceptSuggestion"/> clears the word in progress, so the resulting ContextLost doesn't publish on top of it.</summary>
    private bool _suppressIdlePublish;

    // --- What is on screen, and what it belongs to ---------------------------------------------------

    private IReadOnlyList<Suggestion> _display = Array.Empty<Suggestion>();

    /// <summary>The word the display was computed for: a partial word for completions, "" or a finished word for predictions.</summary>
    private string _displayFor = string.Empty;

    private bool _displayIsIdle;

    // --- Interaction state ---------------------------------------------------------------------------

    /// <summary>A Tab insertion that a further Tab may replace. Null outside the cycle window.</summary>
    private Cycle? _cycle;

    /// <summary>
    /// The word before the caret when WordStrip itself put it there with Tab. It is complete by construction,
    /// so the bar shows what comes after it rather than ways to extend it. Typing more letters onto it makes
    /// it an ordinary word in progress again.
    /// </summary>
    private string? _finishedWord;

    /// <summary>
    /// The last key that reached the application started a line. Tab there is indentation, not a request for
    /// a prediction, so it is left alone.
    /// </summary>
    private bool _atLineStart = true;

    private ITimer? _cycleTimer;

    private sealed record Cycle(
        IReadOnlyList<Suggestion> Candidates,
        int Index,
        string Inserted,
        string CasingSource,
        nint Focus,
        DateTimeOffset LastAt,
        bool IsIdle);

    /// <summary>Global on/off switch, e.g. from the tray icon's "Pause" menu item. Deliberately not persisted — always starts unpaused.</summary>
    public bool IsPaused { get; set; }

    /// <summary>
    /// How many slots the bar can actually show. The bar decides that from its width and font; the controller
    /// needs it so that the first candidate, the cycle order and the confidence test all describe the
    /// candidates the user can see, not ones trimmed off the end.
    /// </summary>
    public int VisibleSlotLimit { get; set; } = int.MaxValue;

    /// <summary>Which input mechanism is currently feeding this controller. Diagnostics only — no behaviour depends on it.</summary>
    public TextContextSource ContextSource => _context.Source;

    /// <summary>Whether the user is currently cycling candidates with Tab.</summary>
    public bool IsInteractionActive => _cycle is not null;

    /// <summary>Fires with the candidates to show plus caret position; an empty candidate list means "hide the bar."</summary>
    public event EventHandler<SuggestionUpdate>? SuggestionsChanged;

    /// <param name="postToMessageLoop">
    /// Queues work to run after the current keyboard-hook callback has returned. Every text replacement goes
    /// through this, and it is not optional in practice: SendInput issued from inside the hook either gets
    /// discarded (when the triggering key is suppressed) or interleaves with the key still in flight. Defaults
    /// to running inline, which is only appropriate for tests.
    /// </param>
    /// <param name="timeProvider">Clock for the Tab cycle window. Tests substitute a manual one.</param>
    /// <param name="focusIdentity">Identifies the focused control, so a cycle cannot survive focus moving.</param>
    /// <param name="keySynchronousWord">
    /// The word in progress as reconstructed from keystrokes, which is never behind the keyboard. When the
    /// context comes from a text service it can trail the last keystroke by a moment, and committing a
    /// completion against a word one letter short would duplicate that letter. Where the two disagree,
    /// boundary keys and Tab are simply left alone.
    /// </param>
    /// <param name="scheduleAfter">Runs an action after a delay; defaults to a timer posted to the message loop.</param>
    public SuggestionController(
        ITextContextProvider contextProvider,
        PredictionEngine predictionEngine,
        ITextInjector textInjector,
        AppSettings settings,
        Action<Action>? postToMessageLoop = null,
        PersonalLanguageModel? personalLearning = null,
        Prediction.Neural.NeuralRerankCoordinator? neuralReranking = null,
        TimeProvider? timeProvider = null,
        Func<nint>? focusIdentity = null,
        Func<string>? keySynchronousWord = null,
        Action<TimeSpan, Action>? scheduleAfter = null)
        : this(contextProvider, ownsContextProvider: false, predictionEngine, textInjector, settings,
               postToMessageLoop, personalLearning, neuralReranking, timeProvider, focusIdentity,
               keySynchronousWord, scheduleAfter)
    {
    }

    /// <summary>
    /// Convenience overload for the keyboard-hook path. Wraps the session and focus provider in a
    /// <see cref="KeyboardHookTextContextProvider"/> and disposes it along with this controller.
    /// </summary>
    public SuggestionController(
        TypingSession typingSession,
        PredictionEngine predictionEngine,
        ITextInjector textInjector,
        AppSettings settings,
        Action<Action>? postToMessageLoop = null,
        IFocusedControlProvider? focusProvider = null,
        PersonalLanguageModel? personalLearning = null,
        Prediction.Neural.NeuralRerankCoordinator? neuralReranking = null)
        : this(new KeyboardHookTextContextProvider(typingSession, focusProvider), ownsContextProvider: true,
               predictionEngine, textInjector, settings, postToMessageLoop, personalLearning, neuralReranking,
               timeProvider: null, focusIdentity: null, keySynchronousWord: null, scheduleAfter: null)
    {
    }

    private SuggestionController(
        ITextContextProvider contextProvider,
        bool ownsContextProvider,
        PredictionEngine predictionEngine,
        ITextInjector textInjector,
        AppSettings settings,
        Action<Action>? postToMessageLoop,
        PersonalLanguageModel? personalLearning,
        Prediction.Neural.NeuralRerankCoordinator? neuralReranking,
        TimeProvider? timeProvider,
        Func<nint>? focusIdentity,
        Func<string>? keySynchronousWord,
        Action<TimeSpan, Action>? scheduleAfter)
    {
        _context = contextProvider;
        _ownsContextProvider = ownsContextProvider;
        _predictionEngine = predictionEngine;
        _textInjector = textInjector;
        _settings = settings;
        _postToMessageLoop = postToMessageLoop ?? (action => action());
        _personalLearning = personalLearning;
        _neuralReranking = neuralReranking;
        _time = timeProvider ?? TimeProvider.System;
        _focusIdentity = focusIdentity ?? FocusedControlInspector.GetFocusedWindowHandle;
        _keySynchronousWord = keySynchronousWord;
        _scheduleAfter = scheduleAfter;

        _context.CurrentWordChanged += OnCurrentWordChanged;
        _context.WordCommitted += OnWordCommitted;
        _context.ContextLost += OnContextLost;
    }

    private TimeSpan CycleWindow => TimeSpan.FromMilliseconds(_settings.PredictionCycleWindowMs);

    private int CandidateCount => Math.Max(1, Math.Min(_settings.SuggestionCount, VisibleSlotLimit));

    private CompletionThresholds Thresholds => new(
        _settings.CompletionMinPrefixLength, _settings.CompletionMinConfidence, _settings.CompletionMinScoreMargin);

    // --- Keys, as routed from the hook ---------------------------------------------------------------
    // Each returns true when the key was consumed and must not reach the application.

    /// <summary>
    /// Space or a closing punctuation key. Commits the first candidate, followed by the key's own character,
    /// when <see cref="CompletionPolicy"/> is satisfied; otherwise the key is typed as itself.
    /// </summary>
    public bool HandleBoundary(char boundary)
    {
        EndEcho();
        EndCycle(republish: true);
        _atLineStart = false;

        if (!CompletionPolicy.IsCommitBoundary(boundary)) return false;
        if (IsPaused || _dismissed || !_settings.CompleteOnSpace) return false;

        var context = _context.GetContext();
        if (!context.IsSuggestible || context.HasSelection) return false;

        var typed = ResolveWordInProgress(context);
        InteractionLog.Write($"boundary '{boundary}' typed='{typed}' reported='{context.CurrentWord}' displayFor='{_displayFor}' idle={_displayIsIdle} finished='{_finishedWord}' source={context.Source}");
        if (typed.Length == 0 || string.Equals(typed, _finishedWord, StringComparison.Ordinal)) return false;
        EnsureCompletionsFor(typed, context);

        if (CompletionPolicy.SelectForBoundary(
                typed, _display, _predictionEngine.IsCorrectlySpelled, Thresholds, _predictionEngine.GetBestRepairFrequency)
            is not { } pick)
        {
            return false;
        }

        var word = CaseMatching.Apply(typed, pick.Word);
        var inserted = word + boundary;

        // If the field turns out not to hold what we think, the user's own key still has to arrive.
        Replace(typed, inserted, onRefused: () => _textInjector.ReplaceText(string.Empty, boundary.ToString()));

        // The key never reaches TypingSession, so the commit it would have announced — and the learning that
        // hangs off it — happens here instead. The word learned is the one now on screen.
        Learn(word, context.PrecedingWords);

        _finishedWord = null;
        PublishIdle();
        return true;
    }

    /// <summary>
    /// Tab: continues a cycle if one is open, otherwise inserts the first candidate and opens one. Left to the
    /// application in a single-line form field, at the start of a line, with nothing on the bar, or after a
    /// dismissal — Tab must never be taken by a layer the user cannot see.
    /// </summary>
    public bool HandleTab(bool forward)
    {
        if (IsPaused) return false;

        var context = _context.GetContext();

        if (_cycle is { } cycle)
        {
            if (IsCycleValid(cycle, context))
            {
                Advance(cycle, forward);
                return true;
            }

            EndCycle(republish: true);
        }

        // Shift+Tab outside a cycle is the application's: it is how form navigation goes backwards.
        if (!forward) return false;
        if (_dismissed || _display.Count == 0 || !context.IsSuggestible || context.HasSelection) return false;
        if (context.IsSingleLine) return false;

        var typed = ResolveWordInProgress(context);
        InteractionLog.Write($"tab typed='{typed}' reported='{context.CurrentWord}' displayFor='{_displayFor}' idle={_displayIsIdle} lineStart={_atLineStart} source={context.Source}");
        if (_atLineStart && typed.Length == 0) return false;
        if (typed.Length > 0 && !string.Equals(typed, _finishedWord, StringComparison.Ordinal)) EnsureCompletionsFor(typed, context);
        if (!string.Equals(_displayFor, typed, StringComparison.Ordinal)) return false;

        var candidates = WordsOnly(_display);
        if (candidates.Count == 0) return false;

        if (!_displayIsIdle && typed.Length > 0)
        {
            var first = CaseMatching.Apply(typed, candidates[0].Word);

            if (!string.Equals(first, typed, StringComparison.Ordinal))
            {
                Replace(typed, first);
                StartCycle(candidates, first, casingSource: typed, isIdle: false);
                return true;
            }

            // Already the first suggestion. If the strip offers anything else, Tab still means "slot one" and
            // opens the cycle on it, so a quick second Tab reaches slot two: typing "his" with "history"
            // showing, Tab Tab gives "history". This used to jump straight to predicting the next word, which
            // made the second Tab cycle predictions instead - "history" was unreachable, and whether Tab Tab
            // glided along the strip depended on whether what you had typed happened to be a word.
            if (candidates.Count > 1)
            {
                StartCycle(candidates, typed, casingSource: typed, isIdle: false);
                return true;
            }

            // Nothing else on the strip: taking the word as finished and predicting the next is the only
            // useful thing Tab can do.
            var next = WordsOnly(PredictAfter(typed, context));
            if (next.Count == 0) return false;

            Replace(string.Empty, " " + next[0].Word);
            StartCycle(next, next[0].Word, casingSource: string.Empty, isIdle: true);
            return true;
        }

        // Predictions. After a word WordStrip itself completed there is no space yet, so one goes first.
        var lead = typed.Length > 0 ? " " : string.Empty;
        Replace(string.Empty, lead + candidates[0].Word);
        StartCycle(candidates, candidates[0].Word, casingSource: string.Empty, isIdle: true);
        return true;
    }

    /// <summary>
    /// Backspace is always an ordinary delete. It only ends a Tab cycle, like any other key. (It used to undo a
    /// Space completion; that was removed at the owner's request — Ctrl+Backspace already removes a whole
    /// word, and a Backspace that sometimes deletes a character and sometimes restores a word is one more
    /// thing to have to think about.)
    /// </summary>
    public void HandleBackspace()
    {
        EndEcho();
        _atLineStart = false;
        EndCycle(republish: true);
    }

    /// <summary>Esc: ends any interaction and dismisses. Swallowed only when it cancelled an active cycle.</summary>
    public bool HandleEscape()
    {
        var wasActive = _cycle is not null;
        Dismiss();
        return wasActive;
    }

    /// <summary>
    /// Any other key reaching the application — a letter, an arrow, Enter, a shortcut. Ends a cycle and the
    /// undo window before the key is processed, so the cycle can never touch what the user types next.
    /// </summary>
    public void HandleOtherKey(bool isLineBreak)
    {
        EndEcho();
        _atLineStart = isLineBreak;
        EndCycle(republish: true);
    }

    // --- Mouse and system paths ------------------------------------------------------------------------

    /// <summary>A candidate chosen by clicking it. Inserted followed by a space.</summary>
    public void AcceptSuggestion(Suggestion suggestion)
    {

        var context = _context.GetContext();
        var typed = context.CurrentWord;

        // An empty buffer is a legitimate accept when the bar is persistent: the candidates on show are
        // predictions, so there is nothing to replace. Guard on the surface rather than on the buffer, so an
        // accept can never inject where we wouldn't have suggested — and take a stale bar down.
        if (typed.Length == 0 && !context.IsSuggestible)
        {
            Hide();
            return;
        }

        // During a Tab cycle the text before the caret is the candidate WordStrip just inserted. Clicking an
        // alternative swaps that span, exactly as Tab would have.
        if (_cycle is { } cycle)
        {
            EndCycle(republish: false);
            var text = cycle.CasingSource.Length > 0
                ? CaseMatching.Apply(cycle.CasingSource, suggestion.Word)
                : suggestion.Word;

            Replace(cycle.Inserted, text + " ");
            _finishedWord = null;
            PublishIdle();
            return;
        }

        // Predictions shown after a word WordStrip completed: the choice follows that word, it doesn't replace it.
        if (typed.Length > 0 && string.Equals(typed, _finishedWord, StringComparison.Ordinal))
        {
            Replace(string.Empty, " " + suggestion.Word + " ");
            _finishedWord = null;
            PublishIdle();
            return;
        }

        _finishedWord = null;

        // NoteTextInserted rather than discarding the context: the word is about to be typed into the field,
        // so it becomes part of what the next prediction works from. Its ContextLost is silenced and the idle
        // list published once here, so the bar is never updated twice for one accepted word.
        _suppressIdlePublish = true;
        try { _context.NoteTextInserted(suggestion.Word); }
        finally { _suppressIdlePublish = false; }

        PublishIdle();

        _postToMessageLoop(() =>
        {
            if (!_textInjector.ReplaceInProgressWord(typed, suggestion.Word, appendTrailingSpace: true)) Resynchronise();
        });
    }

    /// <summary>
    /// Takes the bar away and keeps it away until the user starts typing another word. This is the Esc key
    /// and the click-outside path; it is deliberately stickier than an ordinary hide, because with a
    /// persistent bar every other code path is trying to put the bar back on screen.
    /// </summary>
    public void Dismiss()
    {
        _dismissed = true;
        Hide();
    }

    /// <summary>
    /// Hides a persistent bar once focus has moved somewhere it doesn't belong. Nothing in the input pipeline
    /// fires when the user Alt+Tabs away; the app polls this on a timer while the bar is up.
    /// </summary>
    public void PollFocus()
    {
        if (!_isShowing) return;
        if (_context.GetContext().IsSuggestible) return;

        // Not a dismissal: focus just went elsewhere, and typing in the next text field brings it straight back.
        Hide();
    }

    // --- The Tab cycle ---------------------------------------------------------------------------------

    private void StartCycle(IReadOnlyList<Suggestion> candidates, string inserted, string casingSource, bool isIdle)
    {
        var cycle = new Cycle(candidates, 0, inserted, casingSource, _focusIdentity(), _time.GetUtcNow(), isIdle);
        EnterCycle(cycle);
    }

    private void Advance(Cycle cycle, bool forward)
    {
        var count = cycle.Candidates.Count;
        var index = ((cycle.Index + (forward ? 1 : -1)) % count + count) % count;

        var text = cycle.CasingSource.Length > 0
            ? CaseMatching.Apply(cycle.CasingSource, cycle.Candidates[index].Word)
            : cycle.Candidates[index].Word;

        // Exactly what was inserted is replaced — never "the word before the caret", never a length guess.
        Replace(cycle.Inserted, text);
        EnterCycle(cycle with { Index = index, Inserted = text, LastAt = _time.GetUtcNow() });
    }

    private void EnterCycle(Cycle cycle)
    {
        _cycle = cycle;
        _finishedWord = LastWordOf(cycle.Inserted);
        InteractionLog.Write($"cycle index={cycle.Index} inserted='{cycle.Inserted}' finished='{_finishedWord}'");
        _dismissed = false;
        _atLineStart = false;

        Show(cycle.Candidates, _context.GetContext().Caret, cycle.IsIdle, _finishedWord, selectedIndex: cycle.Index);
        ScheduleExpiry(cycle);
    }

    /// <summary>
    /// Whether a Tab may still replace the cycle's insertion. The word before the caret must still be the one
    /// inserted — except while that insertion is itself still arriving, when a text service may report it
    /// half-typed. Nothing the user did can have intervened then (any key of theirs ends the echo guard), so
    /// the cycle's own record is the truth.
    /// </summary>
    private bool IsCycleValid(Cycle cycle, TextContext context) =>
        _time.GetUtcNow() - cycle.LastAt <= CycleWindow
        && context.IsSuggestible
        && _focusIdentity() == cycle.Focus
        && (IsEchoing || string.Equals(context.CurrentWord, LastWordOf(cycle.Inserted), StringComparison.Ordinal));

    private void EndCycle(bool republish)
    {
        if (_cycle is null) return;

        _cycle = null;
        _cycleTimer?.Dispose();
        _cycleTimer = null;

        if (republish) PublishIdle();
    }

    private void ScheduleExpiry(Cycle cycle)
    {
        var delay = CycleWindow + TimeSpan.FromMilliseconds(20);

        if (_scheduleAfter is not null)
        {
            _scheduleAfter(delay, () => ExpireCycle(cycle));
            return;
        }

        _cycleTimer?.Dispose();
        _cycleTimer = _time.CreateTimer(
            _ => _postToMessageLoop(() => ExpireCycle(cycle)), null, delay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// The window closed with nothing else happening. The inserted word stays; the bar goes back to passive,
    /// showing what might follow it.
    /// </summary>
    private void ExpireCycle(Cycle cycle)
    {
        if (!ReferenceEquals(_cycle, cycle)) return;  // superseded by a later Tab, or already ended
        if (_time.GetUtcNow() - cycle.LastAt < CycleWindow) return;

        EndCycle(republish: true);
    }

    // --- Provider events -------------------------------------------------------------------------------

    private void OnCurrentWordChanged(object? sender, string word)
    {
        if (InteractionLog.IsEnabled)
        {
            var probe = _context.GetContext();
            InteractionLog.Write($"word '{word}' finished='{_finishedWord}' cycle={_cycle is not null} source={probe.Source} editable={probe.IsEditable} ctxword='{probe.CurrentWord}'");
        }
        // A text service confirming a word WordStrip itself just inserted is not the user typing.
        if (_cycle is not null && string.Equals(word, _finishedWord, StringComparison.Ordinal)) return;

        // Nor is one reporting that insertion arriving letter by letter. In a browser WordStrip's text is
        // typed with synthetic keystrokes, and the document is reported at every step - "f", "fo", "for".
        // Treating those as typing ended the Tab cycle mid-insertion, so a quick second Tab added a word
        // instead of swapping it.
        if (IsEchoing)
        {
            InteractionLog.Write($"echo '{word}' ignored");
            return;
        }

        EndCycle(republish: false);

        if (string.IsNullOrEmpty(word))
        {
            // Fires after every commit as well as when backspacing erases the last character. Either way
            // there is no word in progress, so the bar falls back to whatever it shows between words.
            _finishedWord = null;
            PublishIdle();
            return;
        }

        // Typing is the signal that the user wants the bar back after dismissing it.
        _dismissed = false;
        _atLineStart = false;

        if (IsPaused)
        {
            Hide();
            return;
        }

        var snapshot = _context.GetContext();
        if (!snapshot.IsSuggestible)
        {
            Hide();
            return;
        }

        if (string.Equals(word, _finishedWord, StringComparison.Ordinal))
        {
            PublishIdle();
            return;
        }

        _finishedWord = null;

        // The event's word wins over the snapshot's, so a provider that reads its state asynchronously can
        // never publish suggestions for a word other than the one it just announced.
        PublishCompletions(word, snapshot);
    }

    private void OnWordCommitted(object? sender, WordCommittedEventArgs e)
    {
        // The CurrentWordChanged("") that follows this is what repopulates the bar.
        if (IsPaused) return;

        // Also the privacy gate for learning: a control we would not suggest in is one we must not learn from.
        if (!_context.GetContext().IsSuggestible) return;

        var wordOnScreen = e.Word;

        // A word WordStrip itself inserted is never autocorrected: it came from the dictionary or the user's
        // own list, and "correcting" it is how "Halsted", the last word of a saved address, became "Halted"
        // when a Space followed the insertion.
        var insertedByUs = string.Equals(e.Word, _finishedWord, StringComparison.Ordinal);
        InteractionLog.Write($"committed '{e.Word}' boundary='{e.BoundaryChar}' finished='{_finishedWord}' ours={insertedByUs}");

        var corrected = insertedByUs ? null : CorrectionFor(e);

        if (corrected is not null)
        {
            // The context has to follow the correction, not the typo.
            _context.NoteWordCorrected(corrected);
            wordOnScreen = corrected;

            // Deferred so the boundary key the user just pressed lands in the target app first; correcting
            // from inside the hook callback races that keystroke and garbles the result.
            var typed = e.Word;
            var boundary = e.BoundaryChar;
            _postToMessageLoop(() =>
            {
                if (!_textInjector.ReplaceCommittedWord(typed, boundary, corrected)) Resynchronise();
            });
        }

        // Learn what ended up on screen, not what was typed — otherwise every typo the app just fixed would
        // be taught back to it as vocabulary.
        Learn(wordOnScreen, e.PrecedingWords);
    }

    /// <summary>
    /// What a finished word should become, or null to leave it. In order: the written form of a word that is
    /// only ever written one way ("im" to "I'm", "i" to "I", "london" to "London"); otherwise a spelling
    /// correction, itself in written form; and a capital if the word is known to open a sentence. A word
    /// that is already a written form is never "spell-corrected" — "don't" is not a misspelling of "dont".
    /// </summary>
    private string? CorrectionFor(WordCommittedEventArgs e)
    {
        var forms = _settings.FixCapitalsAndApostrophes;
        var word = forms ? EnglishForms.CorrectionFor(e.Word) : null;

        if (word is null && _settings.AutocorrectEnabled && !EnglishForms.IsKnownForm(e.Word)
            && _predictionEngine.GetAutocorrection(e.Word) is { } fix)
        {
            word = forms ? EnglishForms.ToWritten(fix.Word) : fix.Word;
        }

        if (forms && e.StartsSentence)
            word = EnglishForms.Capitalize(word ?? e.Word);

        return word is null || string.Equals(word, e.Word, StringComparison.Ordinal) ? null : word;
    }

    /// <summary>
    /// At a point known to begin a sentence, suggestions are shown capitalised, which is also how they will be
    /// inserted. Only on knowledge (see <see cref="TextContext.IsSentenceStartKnown"/>): capitalising after a
    /// click that merely might be at a sentence start would be wrong more often than right.
    /// </summary>
    private IReadOnlyList<Suggestion> ForSentenceStart(IReadOnlyList<Suggestion> list, TextContext snapshot)
    {
        if (!_settings.FixCapitalsAndApostrophes || !snapshot.IsSentenceStartKnown || list.Count == 0) return list;

        return list
            .Select(s => s.IsEmoji ? s : s with { Word = EnglishForms.Capitalize(s.Word) })
            .ToList();
    }

    private void OnContextLost(object? sender, EventArgs e)
    {
        InteractionLog.Write($"context lost (finished was '{_finishedWord}')");
        EndCycle(republish: false);
        _finishedWord = null;
        PublishIdle();
    }

    // --- Publishing ------------------------------------------------------------------------------------

    private void PublishCompletions(string word, TextContext snapshot)
    {
        var context = BuildContext(word, snapshot);
        var fresh = ForSentenceStart(
            _predictionEngine.GetLiveSuggestions(word, CandidateCount, context, _settings.EmojiSuggestionsEnabled),
            snapshot);

        // Consecutive states of one word: hold the order steady unless the model is decisively surer.
        var shown = IsSameWordEvolving(word)
            ? CandidateStabilizer.Stabilize(_display, fresh, _settings.RankingHysteresis)
            : fresh;

        Show(shown, snapshot.Caret, isIdle: false, forWord: word);

        // The statistical answer is already on screen. If a neural model is loaded and the answer looked
        // uncertain, ask it in the background and republish only if it still applies.
        RerankInBackground(context, shown, snapshot.Caret);
    }

    /// <summary>
    /// What the bar shows when no word is in progress — or when the word before the caret is one WordStrip
    /// just completed: predictions if it's meant to stay put, nothing if it's meant to appear per-word.
    /// </summary>
    private void PublishIdle()
    {
        if (_suppressIdlePublish) return;

        if (IsPaused || _dismissed || !_settings.PersistentBar)
        {
            Hide();
            return;
        }

        var snapshot = _context.GetContext();
        if (!snapshot.IsSuggestible)
        {
            Hide();
            return;
        }

        var typed = snapshot.CurrentWord;
        if (typed.Length > 0 && !string.Equals(typed, _finishedWord, StringComparison.Ordinal))
        {
            PublishCompletions(typed, snapshot);
            return;
        }

        var context = typed.Length > 0 ? AfterWord(snapshot, typed) : BuildContext(string.Empty, snapshot);
        var predictions = ShapePredictions(_predictionEngine.GetNextWords(
            context, CandidateCount, includePhrases: _settings.PhraseSuggestionsEnabled));

        // After a word WordStrip just inserted, the next word does not begin a sentence, whatever came before.
        if (typed.Length == 0) predictions = ForSentenceStart(predictions, snapshot);

        Show(predictions, snapshot.Caret, isIdle: true, forWord: typed);
    }

    private void Show(IReadOnlyList<Suggestion> list, CaretRect? caret, bool isIdle, string forWord, int selectedIndex = -1)
    {
        if (list.Count > CandidateCount) list = list.Take(CandidateCount).ToList();

        _display = list;
        _displayFor = forWord;
        _displayIsIdle = isIdle;

        var armed = selectedIndex < 0 && !isIdle && IsArmed(forWord, list);
        Publish(new SuggestionUpdate(list, caret, isIdle, selectedIndex, armed));
    }

    private void Hide()
    {
        EndCycle(republish: false);
        _display = Array.Empty<Suggestion>();
        _displayFor = string.Empty;
        _displayIsIdle = false;
        Publish(SuggestionUpdate.Empty);
    }

    private bool IsArmed(string typed, IReadOnlyList<Suggestion> list) =>
        _settings.CompleteOnSpace
        && !IsPaused
        && !_dismissed
        && CompletionPolicy.SelectForBoundary(
            typed, list, _predictionEngine.IsCorrectlySpelled, Thresholds, _predictionEngine.GetBestRepairFrequency) is not null;

    private void Publish(SuggestionUpdate update)
    {
        _isShowing = update.Suggestions.Count > 0;
        SuggestionsChanged?.Invoke(this, update);
    }

    /// <summary>
    /// Asks the neural model to reorder what is already on the bar, and republishes only if the answer is
    /// still relevant when it arrives. Fire-and-forget: the statistical suggestions are already on screen, so
    /// if inference is slow, superseded or broken, nothing happens and nobody notices.
    /// </summary>
    private void RerankInBackground(PredictionContext context, IReadOnlyList<Suggestion> published, CaretRect? caret)
    {
        if (_neuralReranking is null || !_settings.NeuralRerankingEnabled) return;
        if (!_neuralReranking.ShouldRerank(published)) return;

        var word = context.PartialWord;

        _ = Task.Run(async () =>
        {
            var reranked = await _neuralReranking.RerankAsync(context, published).ConfigureAwait(false);
            if (ReferenceEquals(reranked, published)) return;

            _postToMessageLoop(() =>
            {
                // Still the same word, still on screen, and the user isn't mid-cycle? If not, this describes
                // text that has already gone, or would pull the bar out from under a deliberate choice.
                if (!string.Equals(_context.GetContext().CurrentWord, word, StringComparison.Ordinal)) return;
                if (IsPaused || _dismissed || _cycle is not null) return;
                if (_displayIsIdle || !string.Equals(_displayFor, word, StringComparison.Ordinal)) return;

                Show(reranked, caret, isIdle: false, forWord: word);
            });
        });
    }

    // --- Helpers ---------------------------------------------------------------------------------------

    /// <summary>
    /// Makes the provider's picture of the text exact at once, then sends the edit once the hook callback has
    /// returned. The order matters: the next keystroke may be processed before the injection runs, and it has
    /// to be judged against the text as it is about to be.
    /// </summary>
    private void Replace(string existing, string replacement, Action? onRefused = null)
    {
        _echoUntil = _time.GetUtcNow() + EchoWindow;
        _context.NoteTextReplaced(existing, replacement);
        _postToMessageLoop(() =>
        {
            if (_textInjector.ReplaceText(existing, replacement)) return;

            // The field did not contain what WordStrip believed — changed without a keystroke, or the caret
            // is elsewhere. Nothing was edited. What we hold about it is now known to be wrong, so it goes.
            Resynchronise();
            onRefused?.Invoke();
        });
    }

    private void Resynchronise()
    {
        InteractionLog.Write("resynchronise: an edit was refused");
        EndCycle(republish: false);
        _finishedWord = null;
        _context.InvalidateContext();
        PublishIdle();
    }

    private bool IsSameWordEvolving(string word) =>
        !_displayIsIdle
        && _display.Count > 0
        && _displayFor.Length > 0
        && (word.StartsWith(_displayFor, StringComparison.OrdinalIgnoreCase)
            || _displayFor.StartsWith(word, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// How long after one of WordStrip's own edits the provider's reports are taken as that edit arriving,
    /// rather than as the user typing. Long enough for a browser to report a synthetic-keystroke insertion;
    /// irrelevant in practice for anything the user does, because every key of theirs ends it immediately.
    /// </summary>
    private static readonly TimeSpan EchoWindow = TimeSpan.FromMilliseconds(400);

    private DateTimeOffset _echoUntil = DateTimeOffset.MinValue;

    private bool IsEchoing => _time.GetUtcNow() < _echoUntil;

    private void EndEcho() => _echoUntil = DateTimeOffset.MinValue;

    /// <summary>
    /// The word being typed, as best it can be known at this keystroke. A text service reports the document
    /// asynchronously and can be a letter or two behind the keyboard; the keystroke record is never behind.
    /// When the keystrokes simply extend what was last reported, the service is lagging and the keystrokes
    /// are right. Otherwise the reported document wins: the keystroke record is the one that loses track, on
    /// a click or an arrow key. While WordStrip's own edit is still arriving, the keystroke record is the
    /// only one that already includes it.
    /// </summary>
    private string ResolveWordInProgress(TextContext context)
    {
        var reported = context.CurrentWord;
        var keyed = _keySynchronousWord?.Invoke();
        if (keyed is null) return reported;

        if (IsEchoing) return keyed;

        return keyed.Length > reported.Length && keyed.StartsWith(reported, StringComparison.Ordinal)
            ? keyed
            : reported;
    }

    /// <summary>
    /// Makes sure what is on the bar is the completion list for <paramref name="typed"/>, recomputing it if
    /// the provider's update for the latest keystroke has not been processed yet. Decisions are made on the
    /// candidates for the word actually typed, never on the list for the word before it.
    /// </summary>
    private void EnsureCompletionsFor(string typed, TextContext context)
    {
        if (!_displayIsIdle && string.Equals(_displayFor, typed, StringComparison.Ordinal)) return;
        PublishCompletions(typed, context);
    }

    private IReadOnlyList<Suggestion> PredictAfter(string word, TextContext snapshot) =>
        ShapePredictions(_predictionEngine.GetNextWords(
            AfterWord(snapshot, word), CandidateCount, includePhrases: _settings.PhraseSuggestionsEnabled));

    /// <summary>
    /// Longest phrase worth a slot. Beyond this a phrase no longer fits a column and is shortened into
    /// something unreadable ("and t... the"), which is worse than not offering it.
    /// </summary>
    private const int MaxPhraseLength = 10;

    /// <summary>
    /// Keeps the first slot a single word. It is what Tab inserts, and one Tab putting in several words —
    /// then a second Tab swapping them for several others — read as the bar typing on the user's behalf.
    /// Phrases can still sit in the other slots, where choosing one is deliberate, as long as they fit.
    /// </summary>
    internal static IReadOnlyList<Suggestion> ShapePredictions(IReadOnlyList<Suggestion> predictions)
    {
        var kept = predictions.Where(s => !s.IsPhrase || s.Word.Length <= MaxPhraseLength).ToList();
        if (kept.Count == 0 || !kept[0].IsPhrase) return kept;

        var firstWord = kept.FindIndex(s => !s.IsPhrase && !s.IsEmoji);
        if (firstWord > 0)
        {
            var word = kept[firstWord];
            kept.RemoveAt(firstWord);
            kept.Insert(0, word);
            return kept;
        }

        // Only phrases: lead with the first word of the best one.
        var lead = kept[0].Word.Split(' ')[0];
        kept.Insert(0, kept[0] with { Word = lead, Source = SuggestionSource.FrequentWord });
        return kept;
    }

    private static IReadOnlyList<Suggestion> WordsOnly(IReadOnlyList<Suggestion> list) =>
        list.Where(s => !s.IsEmoji).ToList();

    private static string LastWordOf(string text)
    {
        var start = text.Length;
        while (start > 0 && KeyTranslator.IsWordCharacter(text[start - 1])) start--;
        return text[start..];
    }

    /// <summary>Feeds one finished word to the personal model, if the user has asked for that.</summary>
    private void Learn(string word, IReadOnlyList<string> precedingWords)
    {
        if (_personalLearning is null || !_settings.PersonalLearningEnabled) return;

        _personalLearning.Learn(word, precedingWords);
    }

    /// <summary>
    /// Packages what the input layer knows into the value the prediction layer consumes. The partial word is
    /// passed separately so the caller can pin it to the word the provider announced.
    /// </summary>
    private static PredictionContext BuildContext(string partialWord, TextContext snapshot) => new(
        partialWord,
        snapshot.PrecedingWords,
        snapshot.IsAtSentenceStart,
        PrecedingPunctuation: null,
        ShouldCapitalize: snapshot.IsAtSentenceStart);

    /// <summary>The context for predicting what follows <paramref name="word"/>, which has no space after it yet.</summary>
    private static PredictionContext AfterWord(TextContext snapshot, string word)
    {
        var preceding = snapshot.PrecedingWords.Append(word).TakeLast(2).ToArray();
        return new PredictionContext(string.Empty, preceding, IsSentenceStart: false, PrecedingPunctuation: null, ShouldCapitalize: false);
    }

    public void Dispose()
    {
        _cycleTimer?.Dispose();
        _context.CurrentWordChanged -= OnCurrentWordChanged;
        _context.WordCommitted -= OnWordCommitted;
        _context.ContextLost -= OnContextLost;

        if (_ownsContextProvider) _context.Dispose();
    }
}
