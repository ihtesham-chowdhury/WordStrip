using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Personal;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// Ties everything together: watches <see cref="TypingSession"/> for the word currently being typed,
/// asks <see cref="PredictionEngine"/> for candidates, checks <see cref="IFocusedControlProvider"/> so
/// suggestions never appear over an unsupported control or a password field, and performs replacements
/// via <see cref="ITextInjector"/> when a suggestion is accepted or an autocorrect fires. This is the
/// only class the UI layer needs to talk to — it never touches hooks or the prediction engine directly.
/// Reads suggestion count / autocorrect-enabled / persistent-bar live from the shared <see cref="AppSettings"/>
/// instance rather than caching its own copies, so changes made in the settings window take effect on the
/// very next keystroke with no extra event plumbing.
///
/// <para>Between words the strip's content depends on <see cref="AppSettings.PersistentBar"/>: on, it shows
/// common words and stays put; off, it hides until the next word starts. Either way the user can always
/// take it away — see <see cref="Dismiss"/>.</para>
/// </summary>
public sealed class SuggestionController : IDisposable
{
    private readonly TypingSession _typingSession;
    private readonly PredictionEngine _predictionEngine;
    private readonly ITextInjector _textInjector;
    private readonly AppSettings _settings;
    private readonly Action<Action> _postToMessageLoop;
    private readonly IFocusedControlProvider _focusProvider;
    private readonly PersonalLanguageModel? _personalLearning;
    private readonly Prediction.Neural.NeuralRerankCoordinator? _neuralReranking;

    /// <summary>
    /// Set when the user explicitly took the bar away (Esc, or a click outside it) and cleared as soon as
    /// they start typing a word again. Without it, a dismissed bar would immediately come back: dismissing
    /// is usually accompanied by a buffer reset, and the reset itself would republish the idle list.
    /// </summary>
    private bool _dismissed;

    /// <summary>Whether the last thing published was non-empty, i.e. the bar is currently showing something.</summary>
    private bool _isShowing;

    /// <summary>Set while <see cref="AcceptSuggestion"/> resets the buffer, so the resulting BufferReset doesn't publish on top of it.</summary>
    private bool _suppressIdlePublish;

    /// <summary>Global on/off switch, e.g. from the tray icon's "Pause" menu item. Deliberately not persisted — always starts unpaused.</summary>
    public bool IsPaused { get; set; }

    /// <summary>Fires with the candidates to show plus caret position; an empty candidate list means "hide the bar."</summary>
    public event EventHandler<SuggestionUpdate>? SuggestionsChanged;

    /// <param name="postToMessageLoop">
    /// Queues work to run after the current keyboard-hook callback has returned. Every text replacement goes
    /// through this, and it is not optional in practice: this class is driven from inside the low-level
    /// keyboard hook, and SendInput issued from there either gets discarded (when the triggering key is
    /// suppressed) or interleaves with the key still in flight, corrupting the text. Defaults to running
    /// inline, which is only appropriate for tests that drive the controller directly rather than via a hook.
    /// </param>
    /// <param name="focusProvider">
    /// Defaults to live Win32 inspection. Tests supply a fake, since the real one reads the foreground window
    /// and would report "not a text field" under a test runner.
    /// </param>
    public SuggestionController(
        TypingSession typingSession,
        PredictionEngine predictionEngine,
        ITextInjector textInjector,
        AppSettings settings,
        Action<Action>? postToMessageLoop = null,
        IFocusedControlProvider? focusProvider = null,
        PersonalLanguageModel? personalLearning = null,
        Prediction.Neural.NeuralRerankCoordinator? neuralReranking = null)
    {
        _neuralReranking = neuralReranking;
        _typingSession = typingSession;
        _predictionEngine = predictionEngine;
        _textInjector = textInjector;
        _settings = settings;
        _postToMessageLoop = postToMessageLoop ?? (action => action());
        _focusProvider = focusProvider ?? Win32FocusedControlProvider.Instance;
        _personalLearning = personalLearning;

        _typingSession.CurrentWordChanged += OnCurrentWordChanged;
        _typingSession.WordCommitted += OnWordCommitted;
        _typingSession.BufferReset += OnBufferReset;
    }

    /// <summary>Call when the user accepts a candidate from the bar (Tab to highlight, then Space/Enter, or a click).</summary>
    public void AcceptSuggestion(Suggestion suggestion)
    {
        // Snapshot what was typed before clearing state — the replacement runs later, off the hook callback.
        var typed = _typingSession.CurrentWord;

        // An empty buffer is a legitimate accept when the bar is persistent: the candidates on show are
        // common words offered between words, so there is nothing to replace and the word is simply typed.
        // The injector already handles this — no shared prefix means no backspaces. Guard on focus rather
        // than on the buffer, so an accept can never inject into a surface we wouldn't have suggested for.
        if (typed.Length == 0 && !IsSuggestible(_focusProvider.GetFocusedControlInfo()))
        {
            // Nothing to replace, and nowhere sensible to put it. The bar being up at all means it has gone
            // stale — focus moved between the last update and this click — so take it down rather than leave
            // it hovering over a control we would never have offered suggestions for.
            Publish(SuggestionUpdate.Empty);
            return;
        }

        // NoteWordInserted rather than ResetBuffer: the word is about to be typed into the field, so it
        // becomes part of the context the next prediction works from. ResetBuffer would throw that away and
        // the model would go blind for a word every time the user accepted a suggestion.
        //
        // It raises BufferReset, which publishes the idle list by itself — but only when the buffer was
        // non-empty, which it isn't on the between-words accept path. Silence it and publish once here, so
        // both paths behave the same and the bar is never updated twice for one accepted word.
        _suppressIdlePublish = true;
        try { _typingSession.NoteWordInserted(suggestion.Word); }
        finally { _suppressIdlePublish = false; }

        // Straight back to the idle list rather than blanking the bar, so accepting a word doesn't produce
        // the very flicker the persistent bar exists to remove.
        PublishIdle();

        _postToMessageLoop(() => _textInjector.ReplaceInProgressWord(typed, suggestion.Word, appendTrailingSpace: true));
    }

    /// <summary>
    /// Takes the bar away and keeps it away until the user starts typing another word. This is the Esc key
    /// and the click-outside path; it is deliberately stickier than an ordinary hide, because with a
    /// persistent bar every other code path is trying to put the bar back on screen.
    /// </summary>
    public void Dismiss()
    {
        _dismissed = true;
        Publish(SuggestionUpdate.Empty);
    }

    /// <summary>
    /// Hides a persistent bar once focus has moved somewhere it doesn't belong. Nothing in the input pipeline
    /// fires when the user Alt+Tabs away, so a visible bar would otherwise hang over the new window until the
    /// next keystroke; the app polls this on a timer while the bar is up. Cheap enough for ~1 Hz — it is one
    /// GetGUIThreadInfo call and it returns immediately when the bar is already hidden.
    /// </summary>
    public void PollFocus()
    {
        if (!_isShowing) return;
        if (IsSuggestible(_focusProvider.GetFocusedControlInfo())) return;

        // Not a dismissal: the user hasn't rejected the bar, focus just went elsewhere. Leaving _dismissed
        // alone means typing in the next text field brings it straight back.
        Publish(SuggestionUpdate.Empty);
    }

    private void OnCurrentWordChanged(object? sender, string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            // Fires after every commit as well as when backspacing erases the last character. Either way
            // there is no word in progress, so the bar falls back to whatever it shows between words.
            PublishIdle();
            return;
        }

        // Typing is the signal that the user wants the bar back after dismissing it.
        _dismissed = false;

        if (IsPaused)
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        var focus = _focusProvider.GetFocusedControlInfo();
        if (!IsSuggestible(focus))
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        var context = BuildContext(word);
        var suggestions = _predictionEngine.GetLiveSuggestions(
            word, _settings.SuggestionCount, context, _settings.EmojiSuggestionsEnabled);

        Publish(new SuggestionUpdate(suggestions, focus.Caret));

        // The statistical answer is already on screen. If a neural model is loaded and the answer looked
        // uncertain, ask it in the background and republish only if it still applies — never block the
        // keystroke on tens of milliseconds of inference.
        RerankInBackground(context, suggestions, focus.Caret);
    }

    private void OnWordCommitted(object? sender, WordCommittedEventArgs e)
    {
        // The CurrentWordChanged("") that TypingSession raises straight after this is what repopulates the
        // bar, so there is nothing to publish here.
        if (IsPaused) return;

        // One focus check for both jobs below. It is also the privacy gate for learning: a control we would
        // not offer suggestions in — a password box, or anything we cannot identify — is one we must not
        // learn from either.
        var focus = _focusProvider.GetFocusedControlInfo();
        if (!IsSuggestible(focus)) return;

        var wordOnScreen = e.Word;

        if (_settings.AutocorrectEnabled && _predictionEngine.GetAutocorrection(e.Word) is { } correction)
        {
            // The context has to follow the correction, not the typo. TypingSession recorded what was
            // actually typed; once autocorrect decides to rewrite it, the word on screen is the corrected
            // one and that is what the next prediction must be conditioned on.
            _typingSession.ReplaceLastWord(correction.Word);
            wordOnScreen = correction.Word;

            // Deferred so the boundary key the user just pressed lands in the target app first; correcting
            // from inside the hook callback races that keystroke and garbles the result.
            var typed = e.Word;
            var boundary = e.BoundaryChar;
            _postToMessageLoop(() => _textInjector.ReplaceCommittedWord(typed, boundary, correction.Word));
        }

        // Learn what ended up on screen, not what was typed — otherwise every typo the app just fixed would
        // be taught back to it as vocabulary.
        Learn(wordOnScreen, e.PrecedingWords);
    }

    /// <summary>
    /// Feeds one finished word to the personal model, if the user has asked for that.
    ///
    /// <para>Deliberately the only place learning happens. Everything reaching here has been committed by a
    /// real keystroke in a control the app already decided it could suggest in, which is the narrow
    /// definition of "text the user entered" the phase brief asks for — no scraping, no guessing at what is
    /// on screen, nothing learned from a field we could not identify.</para>
    /// </summary>
    private void Learn(string word, IReadOnlyList<string> precedingWords)
    {
        if (_personalLearning is null || !_settings.PersonalLearningEnabled) return;

        _personalLearning.Learn(word, precedingWords);
    }

    private void OnBufferReset(object? sender, EventArgs e) => PublishIdle();

    /// <summary>
    /// What the bar shows when no word is in progress: common words if it's meant to stay put, nothing if
    /// it's meant to appear per-word. Purely frequency-based for now — <see cref="PredictionEngine.GetFrequentWords"/>
    /// is the seam where context-aware predictions land later.
    /// </summary>
    private void PublishIdle()
    {
        if (_suppressIdlePublish) return;

        if (IsPaused || _dismissed || !_settings.PersistentBar)
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        var focus = _focusProvider.GetFocusedControlInfo();
        if (!IsSuggestible(focus))
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        Publish(new SuggestionUpdate(
            _predictionEngine.GetNextWords(
                BuildContext(string.Empty),
                _settings.SuggestionCount,
                includePhrases: _settings.PhraseSuggestionsEnabled),
            focus.Caret,
            IsIdle: true));
    }

    /// <summary>
    /// Asks the neural model to reorder what is already on the bar, and republishes only if the answer is
    /// still relevant when it arrives.
    ///
    /// <para>Deliberately fire-and-forget. The statistical suggestions have already been published, so the
    /// user sees something immediately and inference is pure upside — if it is slow, cancelled, superseded
    /// or broken, nothing happens and nobody notices. Awaiting it here would put tens of milliseconds of
    /// model inference inside a keyboard hook callback, which is the one thing this whole design exists to
    /// avoid.</para>
    ///
    /// <para>The guard before republishing is what stops the bar rearranging itself under the user: by the
    /// time an answer comes back they may have typed on, and the suggestions on screen would then be for a
    /// different word entirely.</para>
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
                // Still the same word being typed? If not, this describes text that has already gone.
                if (!string.Equals(_typingSession.CurrentWord, word, StringComparison.Ordinal)) return;
                if (IsPaused || _dismissed) return;

                Publish(new SuggestionUpdate(reranked, caret));
            });
        });
    }

    /// <summary>
    /// Packages what the typing layer knows into the value the prediction layer consumes. Everything here
    /// comes from <see cref="TypingSession"/> rather than from reading the focused application, so the
    /// context is exactly as trustworthy as the word buffer is — and goes away at the same moment.
    /// </summary>
    private PredictionContext BuildContext(string partialWord) => new(
        partialWord,
        _typingSession.RecentWords,
        _typingSession.IsAtSentenceStart,
        PrecedingPunctuation: null,
        ShouldCapitalize: _typingSession.IsAtSentenceStart);

    private static bool IsSuggestible(FocusedControlInfo focus) =>
        focus.IsStandardEditControl && !focus.IsPasswordField;

    private void Publish(SuggestionUpdate update)
    {
        _isShowing = update.Suggestions.Count > 0;
        SuggestionsChanged?.Invoke(this, update);
    }

    public void Dispose()
    {
        _typingSession.CurrentWordChanged -= OnCurrentWordChanged;
        _typingSession.WordCommitted -= OnWordCommitted;
        _typingSession.BufferReset -= OnBufferReset;
    }
}
