using WordStrip.Core.Automation;
using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Settings;

namespace WordStrip.Core.Suggestions;

/// <summary>
/// Ties everything together: watches <see cref="TypingSession"/> for the word currently being typed,
/// asks <see cref="PredictionEngine"/> for candidates, checks <see cref="FocusedControlInspector"/> so
/// suggestions never appear over an unsupported control or a password field, and performs replacements
/// via <see cref="ITextInjector"/> when a suggestion is accepted or an autocorrect fires. This is the
/// only class the UI layer needs to talk to — it never touches hooks or the prediction engine directly.
/// Reads suggestion count / autocorrect-enabled live from the shared <see cref="AppSettings"/> instance
/// rather than caching its own copies, so changes made in the settings window take effect on the very
/// next keystroke with no extra event plumbing.
/// </summary>
public sealed class SuggestionController : IDisposable
{
    private readonly TypingSession _typingSession;
    private readonly PredictionEngine _predictionEngine;
    private readonly ITextInjector _textInjector;
    private readonly AppSettings _settings;
    private readonly Action<Action> _postToMessageLoop;

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
    public SuggestionController(
        TypingSession typingSession,
        PredictionEngine predictionEngine,
        ITextInjector textInjector,
        AppSettings settings,
        Action<Action>? postToMessageLoop = null)
    {
        _typingSession = typingSession;
        _predictionEngine = predictionEngine;
        _textInjector = textInjector;
        _settings = settings;
        _postToMessageLoop = postToMessageLoop ?? (action => action());

        _typingSession.CurrentWordChanged += OnCurrentWordChanged;
        _typingSession.WordCommitted += OnWordCommitted;
        _typingSession.BufferReset += OnBufferReset;
    }

    /// <summary>Call when the user accepts a candidate from the bar (Tab to highlight, then Space/Enter, or a click).</summary>
    public void AcceptSuggestion(Suggestion suggestion)
    {
        // Snapshot what was typed before clearing state — the replacement runs later, off the hook callback.
        var typed = _typingSession.CurrentWord;
        if (typed.Length == 0) return;

        _typingSession.ResetBuffer();
        Publish(SuggestionUpdate.Empty);

        _postToMessageLoop(() => _textInjector.ReplaceInProgressWord(typed, suggestion.Word, appendTrailingSpace: true));
    }

    private void OnCurrentWordChanged(object? sender, string word)
    {
        if (IsPaused || string.IsNullOrEmpty(word))
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        var focus = FocusedControlInspector.GetFocusedControlInfo();
        if (!IsSuggestible(focus))
        {
            Publish(SuggestionUpdate.Empty);
            return;
        }

        var suggestions = _predictionEngine.GetLiveSuggestions(word, _settings.SuggestionCount);
        Publish(new SuggestionUpdate(suggestions, focus.Caret));
    }

    private void OnWordCommitted(object? sender, WordCommittedEventArgs e)
    {
        Publish(SuggestionUpdate.Empty);

        if (IsPaused || !_settings.AutocorrectEnabled) return;
        if (!IsSuggestible(FocusedControlInspector.GetFocusedControlInfo())) return;

        var correction = _predictionEngine.GetAutocorrection(e.Word);
        if (correction is { } s)
        {
            // Deferred so the boundary key the user just pressed lands in the target app first; correcting
            // from inside the hook callback races that keystroke and garbles the result.
            var word = e.Word;
            var boundary = e.BoundaryChar;
            _postToMessageLoop(() => _textInjector.ReplaceCommittedWord(word, boundary, s.Word));
        }
    }

    private void OnBufferReset(object? sender, EventArgs e) => Publish(SuggestionUpdate.Empty);

    private static bool IsSuggestible(FocusedControlInfo focus) =>
        focus.IsStandardEditControl && !focus.IsPasswordField;

    private void Publish(SuggestionUpdate update) => SuggestionsChanged?.Invoke(this, update);

    public void Dispose()
    {
        _typingSession.CurrentWordChanged -= OnCurrentWordChanged;
        _typingSession.WordCommitted -= OnWordCommitted;
        _typingSession.BufferReset -= OnBufferReset;
    }
}
