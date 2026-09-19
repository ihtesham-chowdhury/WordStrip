using System.Runtime.InteropServices;
using WordStrip.Core.Input;
using WordStrip.Core.Suggestions;
using KeyEventArgs = WordStrip.Core.Input.KeyEventArgs; // UseWindowsForms brings in System.Windows.Forms.KeyEventArgs too

namespace WordStrip.App.Coordination;

/// <summary>
/// Turns raw keystrokes into interaction intents for <see cref="SuggestionController"/>, and suppresses the
/// ones it consumes before they reach the focused application. It makes no decisions of its own — what Space,
/// Tab, Backspace and Esc do depends on state only the controller holds, and keeping it there is what lets
/// the whole interaction model be unit-tested without a keyboard.
///
/// This MUST be subscribed to the hook before <see cref="TypingSession.Attach"/> is called. Handlers run in
/// subscription order, and the contract between the two is: whatever this router suppresses, TypingSession
/// skips entirely (it bails on <c>e.Suppress</c>). Reverse the order and TypingSession would process a
/// consumed Space as a real word boundary — committing, autocorrecting and learning a word that never
/// reached the screen in that form.
///
/// <para>Every key that is not consumed is still reported, before TypingSession sees it, because that is what
/// ends a Tab cycle: the cycle may only ever replace text WordStrip inserted, so the moment the user does
/// anything else it has to close — before their keystroke, not after it.</para>
///
/// <para>Nothing here injects text. The controller defers every replacement to the message loop, because
/// SendInput from inside a low-level hook callback is either discarded or interleaved with the key in
/// flight.</para>
/// </summary>
public sealed class BarInputRouter
{
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_BACK = 0x08;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_CAPITAL = 0x14;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RMENU = 0xA5;

    private readonly SuggestionController _controller;

    public BarInputRouter(LowLevelKeyboardHook keyboardHook, SuggestionController controller)
    {
        _controller = controller;
        keyboardHook.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!InteractionLog.IsEnabled)
        {
            Route(e);
            return;
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Route(e);
        if (clock.Elapsed.TotalMilliseconds > 8)
            InteractionLog.Write($"slow key vk=0x{e.VirtualKeyCode:X2} routed in {clock.Elapsed.TotalMilliseconds:0.0} ms");
    }

    private void Route(KeyEventArgs e)
    {
        if (e.IsInjected || e.Suppress) return;

        var vk = e.VirtualKeyCode;

        // A bare modifier changes nothing on screen, so it must not end a cycle: Shift+Tab to go back through
        // the candidates starts with Shift on its own.
        if (vk is VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL or VK_LWIN or VK_RWIN or (>= VK_LSHIFT and <= VK_RMENU))
            return;

        // Ctrl and Alt combinations belong to the application: paste, undo, shortcuts. They end any cycle and
        // are never consumed.
        if (IsDown(VK_CONTROL) || IsDown(VK_MENU))
        {
            _controller.HandleOtherKey(isLineBreak: false);
            return;
        }

        switch (vk)
        {
            case VK_TAB:
                e.Suppress = _controller.HandleTab(forward: !IsDown(VK_SHIFT));
                return;

            case VK_SPACE:
                e.Suppress = _controller.HandleBoundary(' ');
                return;

            case VK_BACK:
                _controller.HandleBackspace();
                return;

            // Swallowed only when it cancelled an active cycle. With the bar passive, Esc dismisses it AND
            // reaches the app, so it still closes a dialog — a permanently visible bar must not permanently
            // eat Esc. Either way the dismissal is sticky until the user types again.
            case VK_ESCAPE:
                e.Suppress = _controller.HandleEscape();
                return;

            // Never carries a completion: Enter submits forms and sends messages, and rewriting text at the
            // instant it is sent is the one surprise that cannot be taken back.
            case VK_RETURN:
                _controller.HandleOtherKey(isLineBreak: true);
                return;
        }

        if (MayBePunctuation(vk)
            && KeyTranslator.TryTranslateToChar(vk, e.ScanCode, preserveKeyboardState: true) is { } ch
            && CompletionPolicy.IsCommitBoundary(ch))
        {
            e.Suppress = _controller.HandleBoundary(ch);
            return;
        }

        _controller.HandleOtherKey(isLineBreak: false);
    }

    /// <summary>
    /// Keys that can produce closing punctuation on common layouts: the OEM punctuation block, and the digit
    /// row, whose shifted forms include "!" and ")". Translating only these keeps the extra layout lookup off
    /// every letter, and away from the keys most layouts use as dead keys.
    /// </summary>
    private static bool MayBePunctuation(int vk) =>
        vk is (>= 0x30 and <= 0x39) or (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDF) or 0xE2;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
