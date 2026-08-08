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
    private bool _isBarActive;

    public BarInputRouter(LowLevelKeyboardHook keyboardHook, SuggestionController controller, SuggestionBarWindow barWindow)
    {
        _controller = controller;
        _barWindow = barWindow;

        keyboardHook.KeyDown += OnKeyDown;
        _controller.SuggestionsChanged += (_, update) => _isBarActive = update.Suggestions.Count > 0;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.IsInjected || !_isBarActive) return;

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
            // resulting SuggestionsChanged clears _isBarActive through the subscription above.
            case VK_ESCAPE:
                e.Suppress = true;
                _controller.Dismiss();
                break;
        }
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
