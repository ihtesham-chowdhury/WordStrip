using System.Text;
using WordStrip.Core.Input;
using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Automation;

/// <summary>
/// Inspects the currently focused control using plain Win32 (GetGUIThreadInfo + window class + style bits)
/// rather than full UI Automation. This deliberately matches the MVP scope: standard Win32 "Edit"/"RichEdit"
/// controls first (Notepad, most desktop app text boxes/dialogs), which this approach detects reliably and
/// cheaply. Broader coverage (browsers, Office's own text surfaces, etc.) can layer UI Automation on top later
/// without changing this contract.
/// </summary>
public static class FocusedControlInspector
{
    // Validated against real apps: Notepad's (Windows 11) text control reports "RichEditD2DPT", matching
    // the "RichEdit" prefix here as intended. Chromium-based apps ("Chrome_WidgetWin_1") and bare XAML
    // input-routing elements ("Windows.UI.Input.InputSite.WindowClass") correctly fall outside MVP scope.
    private static readonly string[] EditClassPrefixes = { "Edit", "RichEdit", "RICHEDIT" };

    public static FocusedControlInfo GetFocusedControlInfo()
    {
        var foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == 0)
            return default;

        var threadId = GetWindowThreadProcessId(foregroundWindow, out _);

        var info = new GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(threadId, ref info) || info.hwndFocus == 0)
            return default;

        var className = GetWindowClassName(info.hwndFocus);
        var isEdit = EditClassPrefixes.Any(prefix => className.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (!isEdit)
            return default;

        var style = GetWindowLong(info.hwndFocus, GWL_STYLE);
        var isPassword = (style & ES_PASSWORD) != 0;

        return new FocusedControlInfo(
            IsStandardEditControl: true,
            IsPasswordField: isPassword,
            Caret: TryGetCaretScreenRect(info),
            Handle: info.hwndFocus);
    }

    /// <summary>
    /// Converts the caret rectangle reported by GetGUIThreadInfo (client coordinates of the caret's owning
    /// window) into screen pixels, so the bar can position itself relative to where text is being typed.
    /// Returns null when the focused control doesn't report a usable caret — plenty of controls don't, which
    /// is why caret-following is an opt-in placement mode rather than the default.
    /// </summary>
    private static CaretRect? TryGetCaretScreenRect(in GUITHREADINFO info)
    {
        var caretOwner = info.hwndCaret != 0 ? info.hwndCaret : info.hwndFocus;
        if (caretOwner == 0) return null;

        var rect = info.rcCaret;
        if (rect.right <= rect.left && rect.bottom <= rect.top) return null;

        var topLeft = new POINT { x = rect.left, y = rect.top };
        var bottomRight = new POINT { x = rect.right, y = rect.bottom };
        if (!ClientToScreen(caretOwner, ref topLeft) || !ClientToScreen(caretOwner, ref bottomRight))
            return null;

        return new CaretRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
    }

    private static string GetWindowClassName(nint hwnd)
    {
        var buffer = new StringBuilder(256);
        var length = GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }
}
