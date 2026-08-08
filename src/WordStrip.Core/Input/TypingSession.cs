using System.Text;
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
    private readonly LowLevelKeyboardHook _keyboardHook;
    private readonly LowLevelMouseHook _mouseHook;
    private readonly StringBuilder _currentWord = new();
    private bool _ownsHooks;

    public event EventHandler<string>? CurrentWordChanged;
    public event EventHandler<WordCommittedEventArgs>? WordCommitted;
    public event EventHandler? BufferReset;

    public string CurrentWord => _currentWord.ToString();

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

    /// <summary>Call after externally replacing the in-progress word (e.g. via <see cref="ITextInjector"/>) so the buffer matches the new reality.</summary>
    public void ResetBuffer()
    {
        if (_currentWord.Length == 0) return;
        _currentWord.Clear();
        BufferReset?.Invoke(this, EventArgs.Empty);
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
                // what's in the field near the caret, so give up tracking until the next boundary.
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
