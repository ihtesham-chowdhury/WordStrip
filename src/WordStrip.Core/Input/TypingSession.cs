using System.Text;
using WordStrip.Core.Prediction.NGram;
using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

/// <summary>
/// Consumes raw key events from <see cref="LowLevelKeyboardHook"/> (and click events from
/// <see cref="LowLevelMouseHook"/>) and reconstructs "the word currently being typed" as a local buffer.
/// This buffer is a best-effort shadow of the real caret context, not a read of the actual text field —
/// anything that could desync it (arrow keys, clicks, Ctrl/Alt combos like paste or select-all) resets it
/// rather than guessing, which is the deliberate MVP tradeoff for staying out of full UI Automation.
/// </summary>
public sealed class TypingSession : IDisposable
{
    /// <summary>
    /// How many finished words to remember. Two is exactly what a trigram model consumes, and remembering
    /// more would be storing the user's typing for no purpose — the privacy posture is that only the word in
    /// progress is held, and this stretches that as little as the feature allows.
    /// </summary>
    private const int MaxHistoryLength = 2;

    private readonly LowLevelKeyboardHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StringBuilder _currentWord = new();
    private readonly List<string> _recentWords = new(MaxHistoryLength);
    private bool _ownsHooks;
    private bool _atSentenceStart = true;

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
    public event EventHandler? BufferReset;

    public string CurrentWord => _currentWord.ToString();

    /// <summary>
    /// The last finished words before the caret, oldest first — the context an n-gram model needs.
    ///
    /// <para>Held to exactly the same standard as <see cref="CurrentWord"/>: this is a shadow of the real
    /// text, and the moment anything could have moved the caret out from under it, it is dropped rather than
    /// guessed at. Stale history is worse than none, because predicting confidently from words that are no
    /// longer behind the cursor is indistinguishable from the model being wrong.</para>
    /// </summary>
    public IReadOnlyList<string> RecentWords => _recentWords;

    /// <summary>Whether the caret is at the start of a sentence — after a full stop, or wherever tracking last restarted.</summary>
    public bool IsAtSentenceStart => _atSentenceStart;

    /// <summary>
    /// Does NOT subscribe to the hooks yet — call <see cref="Attach"/> once any other hook subscriber that
    /// needs to run first (e.g. a UI-layer Tab/Enter/Esc router) has already subscribed. Event handlers fire
    /// in subscription order, and that ordering matters here: see <c>BarInputRouter</c>'s remarks.
    /// </summary>
    public TypingSession(LowLevelKeyboardHook keyboardHook, LowLevelMouseHook mouseHook)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
    }

    /// <summary>Subscribes to the hooks passed at construction. Must be called before typing is tracked; deliberately separate from the constructor so callers control subscription order relative to other hook consumers.</summary>
    public void Attach()
    {
        _keyboardHook.KeyDown += OnKeyDown;
        _mouseHook.MouseButtonDown += OnMouseButtonDown;
    }

    /// <summary>Convenience factory for simple cases with no ordering concerns: creates its own hooks, attaches, installs, and disposes them along with this session.</summary>
    public static TypingSession CreateAndInstall()
    {
        var keyboardHook = new LowLevelKeyboardHook();
        var mouseHook = new LowLevelMouseHook();
        var session = new TypingSession(keyboardHook, mouseHook) { _ownsHooks = true };
        session.Attach();
        keyboardHook.Install();
        mouseHook.Install();
        return session;
    }

    /// <summary>
    /// Drops everything we think we know about the text near the caret: the in-progress word and the
    /// preceding-word history both go. Called when something happened that could have moved the caret
    /// anywhere — a click, an arrow key, a paste.
    ///
    /// <para>The history is cleared even when the buffer was already empty, which is the common case for a
    /// click between words. The <see cref="BufferReset"/> event still fires only when there was a buffer to
    /// reset, because the bar's visibility keys off that and firing it more often would change when the
    /// strip repaints.</para>
    /// </summary>
    public void ResetBuffer()
    {
        var hadBuffer = _currentWord.Length > 0;

        _currentWord.Clear();
        _recentWords.Clear();

        // With no history the model answers as if a sentence were starting, which is the most defensible
        // guess when the caret's surroundings are genuinely unknown.
        _atSentenceStart = true;

        if (hadBuffer) BufferReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Records a word that reached the text field without being typed key by key — an accepted suggestion.
    /// The buffer is cleared as <see cref="ResetBuffer"/> would, but the word joins the history instead of
    /// wiping it, because unlike a stray click this is a case where we know exactly what is now behind the
    /// caret.
    /// </summary>
    public void NoteWordInserted(string word)
    {
        var hadBuffer = _currentWord.Length > 0;
        _currentWord.Clear();

        PushHistory(word);
        _atSentenceStart = false;

        if (hadBuffer) BufferReset?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Corrects the most recent history entry after autocorrect rewrote it, so the model predicts from what
    /// actually ended up on screen rather than from the typo the user made.
    /// </summary>
    public void ReplaceLastWord(string corrected)
    {
        if (_recentWords.Count == 0) return;

        var normalized = (corrected ?? string.Empty).Trim();
        if (normalized.Length == 0) return;

        _recentWords[^1] = normalized;
    }

    private void PushHistory(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return;

        _recentWords.Add(word);
        while (_recentWords.Count > MaxHistoryLength)
            _recentWords.RemoveAt(0);
    }

    private void OnMouseButtonDown(object? sender, EventArgs e) => ResetBuffer();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.IsInjected) return; // our own SendInput output — already reflected in the buffer by whoever triggered it

        // An earlier subscriber (the suggestion bar's input router) already claimed this key and is swallowing
        // it, so the focused text field never sees it — meaning the real text is unchanged and our shadow buffer
        // must not react either. Without this, the router's Tab-to-cycle would also hit the Tab branch below,
        // reset the buffer, and tear the bar down mid-interaction.
        if (e.Suppress) return;

        var vk = e.VirtualKeyCode;

        if (vk is VK_SHIFT or VK_CONTROL or VK_MENU)
            return; // bare modifier press, nothing to do yet

        if (IsModifierComboActive())
        {
            // Ctrl/Alt combos (paste, select-all, undo, app shortcuts, ...) can change the text field in ways
            // we have no visibility into — safest is to drop our buffer rather than let it drift out of sync.
            ResetBuffer();
            return;
        }

        if (vk == VK_BACK)
        {
            if (_currentWord.Length > 0)
            {
                _currentWord.Length -= 1;
                CurrentWordChanged?.Invoke(this, CurrentWord);
            }
            else
            {
                // Backspacing into text we weren't tracking (e.g. the previous word) — we no longer know
                // what's in the field near the caret, so give up tracking until the next boundary. The
                // history goes too: the caret is now inside the word that history's last entry describes,
                // so predicting from it would be predicting from text being edited away.
                _recentWords.Clear();
                _atSentenceStart = true;
                BufferReset?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        if (KeyTranslator.IsWordBoundaryKey(vk))
        {
            CommitIfNonEmpty(vk == VK_SPACE ? ' ' : vk == VK_RETURN ? '\n' : '\t');
            return;
        }

        if (KeyTranslator.IsContextInvalidatingKey(vk))
        {
            ResetBuffer();
            return;
        }

        var ch = KeyTranslator.TryTranslateToChar(vk, e.ScanCode);
        if (ch is null) return;

        if (KeyTranslator.IsWordCharacter(ch.Value))
        {
            _currentWord.Append(ch.Value);
            CurrentWordChanged?.Invoke(this, CurrentWord);
        }
        else
        {
            // Punctuation etc. acts as a word boundary too (e.g. "hello," should offer to correct "hello").
            CommitIfNonEmpty(ch.Value);
        }
    }

    private void CommitIfNonEmpty(char boundaryChar)
    {
        if (_currentWord.Length == 0) return;

        var word = CurrentWord;
        _currentWord.Clear();

        // A full stop ends the context as surely as a click does. The previous sentence's last words say
        // nothing useful about how the next one opens, so they are dropped and the model is told a sentence
        // is beginning — which it can answer far better than raw word frequency can.
        if (NGramTokenizer.IsSentenceTerminator(boundaryChar))
        {
            _recentWords.Clear();
            _atSentenceStart = true;
        }
        else
        {
            PushHistory(word);
            _atSentenceStart = false;
        }

        WordCommitted?.Invoke(this, new WordCommittedEventArgs { Word = word, BoundaryChar = boundaryChar });
        CurrentWordChanged?.Invoke(this, CurrentWord);
    }

    private static bool IsModifierComboActive() =>
        (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

    public void Dispose()
    {
        _keyboardHook.KeyDown -= OnKeyDown;
        _mouseHook.MouseButtonDown -= OnMouseButtonDown;

        if (_ownsHooks)
        {
            _keyboardHook.Dispose();
            _mouseHook.Dispose();
        }
    }
}
