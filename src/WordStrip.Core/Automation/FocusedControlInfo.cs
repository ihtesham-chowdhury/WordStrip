namespace WordStrip.Core.Automation;

/// <summary>A caret rectangle in screen pixels.</summary>
public readonly record struct CaretRect(int Left, int Top, int Right, int Bottom)
{
    public int Height => Bottom - Top;
}

/// <param name="Handle">
/// The focused control's window handle, or zero when there isn't one.
///
/// <para>Carried so text can be delivered to the control directly rather than pretended at the keyboard.
/// Synthetic keystrokes are dragged through every low-level keyboard hook on the machine, which measured at
/// two milliseconds per character — two hundred for an address — and some editors mangle a burst that long.
/// A window message goes straight to the control.</para>
/// </param>
/// <param name="IsRichEdit">
/// Whether this is a RichEdit rather than a plain Edit. The two disagree about how a backspace must be
/// delivered as a message, and there is no form that works for both — see <c>Win32TextInjector</c>.
/// </param>
public readonly record struct FocusedControlInfo(
    bool IsStandardEditControl,
    bool IsPasswordField,
    CaretRect? Caret = null,
    nint Handle = 0,
    bool IsRichEdit = false);
