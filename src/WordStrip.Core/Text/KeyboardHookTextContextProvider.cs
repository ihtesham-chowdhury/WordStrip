using WordStrip.Core.Automation;
using WordStrip.Core.Input;

namespace WordStrip.Core.Text;

/// <summary>
/// The input path WordStrip has always used, behind the Phase 7 abstraction: a low-level keyboard hook
/// reconstructing the word in progress, plus a Win32 query for what has focus.
///
/// <para>Pure adapter. Every behaviour lives in <see cref="TypingSession"/> and
/// <see cref="IFocusedControlProvider"/> exactly as before — this contributes no logic of its own, which is
/// deliberate. The abstraction had to be introduced without changing what the existing path does, because
/// that path is the fallback that must keep working everywhere TSF does not reach.</para>
///
/// <para><b>It does not own the typing session.</b> The session's hook subscription order relative to the
/// bar's input router is load-bearing (see <c>BarInputRouter</c>), so the composition root creates and
/// attaches it; this only listens. Disposing this unsubscribes and leaves the session running.</para>
/// </summary>
public sealed class KeyboardHookTextContextProvider : ITextContextProvider
{
    private readonly TypingSession _typingSession;
    private readonly IFocusedControlProvider _focusProvider;

    /// <param name="focusProvider">
    /// Defaults to live Win32 inspection. Tests supply a fake, since the real one reads the foreground window
    /// and would report "not a text field" under a test runner.
    /// </param>
    public KeyboardHookTextContextProvider(
        TypingSession typingSession,
        IFocusedControlProvider? focusProvider = null)
    {
        _typingSession = typingSession;
        _focusProvider = focusProvider ?? Win32FocusedControlProvider.Instance;

        _typingSession.CurrentWordChanged += OnCurrentWordChanged;
        _typingSession.WordCommitted += OnWordCommitted;
        _typingSession.BufferReset += OnBufferReset;
    }

    public TextContextSource Source => TextContextSource.KeyboardHook;

    /// <summary>
    /// Always true. A low-level keyboard hook observes every keystroke on the desktop, so there is no
    /// application this provider cannot watch — which is exactly why it is the fallback.
    ///
    /// <para>Whether the focused control is one WordStrip may <em>suggest</em> in is a different question
    /// entirely, answered per call by <see cref="TextContext.IsSuggestible"/>. Conflating the two would make
    /// the fallback appear to drop out every time the user clicked on a button.</para>
    /// </summary>
    public bool IsAvailable => true;

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
    public event EventHandler? ContextLost;

    public TextContext GetContext()
    {
        var focus = _focusProvider.GetFocusedControlInfo();

        return new TextContext(
            IsEditable: focus.IsStandardEditControl,
            IsPasswordField: focus.IsPasswordField,
            CurrentWord: _typingSession.CurrentWord,
            PrecedingWords: _typingSession.RecentWords,
            IsAtSentenceStart: _typingSession.IsAtSentenceStart,
            Caret: focus.Caret,
            Source: TextContextSource.KeyboardHook,

            // A keystroke observer cannot see a selection made with the mouse, and reporting a confident
            // "false" for something unknown is the lesser evil only because every consumer currently treats
            // this as "nothing special to avoid". A TSF provider can answer it properly.
            HasSelection: false);
    }

    /// <summary>
    /// Records an accepted suggestion as the word now behind the caret, rather than discarding the context.
    /// Raises <see cref="ContextLost"/> when a partly-typed word was replaced, matching what the underlying
    /// session has always done.
    /// </summary>
    public void NoteTextInserted(string text) => _typingSession.NoteWordInserted(text);

    public void NoteWordCorrected(string correctedWord) => _typingSession.ReplaceLastWord(correctedWord);

    private void OnCurrentWordChanged(object? sender, string word) => CurrentWordChanged?.Invoke(this, word);

    private void OnWordCommitted(object? sender, WordCommittedEventArgs e) => WordCommitted?.Invoke(this, e);

    private void OnBufferReset(object? sender, EventArgs e) => ContextLost?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        _typingSession.CurrentWordChanged -= OnCurrentWordChanged;
        _typingSession.WordCommitted -= OnWordCommitted;
        _typingSession.BufferReset -= OnBufferReset;
    }
}
