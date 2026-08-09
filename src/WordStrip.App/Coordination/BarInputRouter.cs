using System.Runtime.InteropServices;
using WordStrip.App.UI;
using WordStrip.Core.Input;
using WordStrip.Core.Prediction;
using WordStrip.Core.Suggestions;
using KeyEventArgs = WordStrip.Core.Input.KeyEventArgs; // UseWindowsForms brings in System.Windows.Forms.KeyEventArgs too

namespace WordStrip.App.Coordination;

/// <summary>
/// Owns the Tab-cycle / Enter-accept / Esc-dismiss keyboard interaction with the suggestion bar. Subscribes
/// to the raw keyboard hook directly (rather than going through <see cref="TypingSession"/>) so it can
/// suppress Tab/Enter/Esc before they reach the focused application.
///
/// This MUST be subscribed to the hook before <see cref="TypingSession.Attach"/> is called. Handlers run in
/// subscription order, and the contract between the two is: whatever this router suppresses, TypingSession
/// skips entirely (it bails on <c>e.Suppress</c>). Reverse the order and TypingSession would process the same
/// Tab first — resetting its buffer and tearing the bar down before the user finishes cycling candidates.
///
/// <para>"The bar is visible" and "the bar owns the keyboard" are deliberately not the same condition. They
/// used to be, because the bar was only ever up while a word was being typed; a persistent bar is up almost
/// continuously, and a router keyed on mere visibility would hold Tab and Esc hostage the entire time.</para>
/// </summary>
public sealed class BarInputRouter
{
    private const int VK_TAB = 0x09;
    private const int VK_RETURN = 0x0D;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SHIFT = 0x10;
    private const int VK_SPACE = 0x20;

    private readonly SuggestionController _controller;
    private readonly SuggestionBarWindow _barWindow;

    /// <summary>The bar is showing completions for a word in progress, and therefore owns Tab/Space/Enter/Esc.</summary>
    private bool _isCompleting;

    /// <summary>The bar is on screen between words. Visible, but claims no keys — see <see cref="OnKeyDown"/>.</summary>
    private bool _isIdleVisible;

    public BarInputRouter(LowLevelKeyboardHook keyboardHook, SuggestionController controller, SuggestionBarWindow barWindow)
    {
        _controller = controller;
        _barWindow = barWindow;

        keyboardHook.KeyDown += OnKeyDown;
        _controller.SuggestionsChanged += (_, update) =>
        {
            var visible = update.Suggestions.Count > 0;
            _isCompleting = visible && !update.IsIdle;
            _isIdleVisible = visible && update.IsIdle;
        };
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.IsInjected) return;

        // A persistent bar is on screen for most of the time the user spends in a text field, so it must not
        // behave like a modal input surface while it sits there between words: swallowing Tab would stop it
        // indenting and moving between dialog fields, and swallowing Esc would stop it closing dialogs. Only
        // a bar offering completions for a word in progress claims keys. Between words the mouse is the way
        // in — clicking a word inserts it — and Esc is honoured without being consumed, so pressing it in a
        // dialog both puts the bar away and closes the dialog, which is what the user meant either way.
        if (!_isCompleting)
        {
            if (_isIdleVisible && e.VirtualKeyCode == VK_ESCAPE)
                _controller.Dismiss();
            return;
        }

        switch (e.VirtualKeyCode)
        {
            case VK_TAB:
                e.Suppress = true;
                var forward = (GetAsyncKeyState(VK_SHIFT) & 0x8000) == 0;
                _barWindow.CycleSelection(forward);
                break;

            // Space is the primary accept key — it's under the thumb, unlike Enter. Both only accept while a
            // candidate is actually highlighted (i.e. the user has pressed Tab at least once). Without that
            // guard, every space you type would rewrite the word you just typed, which would be unusable.
            // With no selection these fall through untouched: space commits the word and autocorrect runs.
            case VK_SPACE when _barWindow.HasSelection:
            case VK_RETURN when _barWindow.HasSelection:
                e.Suppress = true;
                // Safe to call inline: the controller defers the actual text injection off this hook callback.
                if (_barWindow.GetSelectedSuggestion() is { } selected)
                    _controller.AcceptSuggestion(selected);
                break;

            // Routed through the controller rather than hiding the window directly: with a persistent bar,
            // hiding the window alone doesn't stick — the controller would put the idle list straight back
            // on the next buffer reset. Dismiss() is what keeps it away until the user types again. The
            // resulting SuggestionsChanged clears the state flags through the subscription above.
            case VK_ESCAPE:
                e.Suppress = true;
                _controller.Dismiss();
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
