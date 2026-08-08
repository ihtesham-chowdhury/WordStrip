using System.Windows;
using System.Windows.Interop;
using static WordStrip.App.Interop.DwmNativeMethods;

namespace WordStrip.App.Interop;

/// <summary>
/// Applies the window-level behaviour the floating strip needs:
///   - Never steals keyboard focus (WS_EX_NOACTIVATE) or shows in the taskbar/Alt+Tab (WS_EX_TOOLWINDOW) —
///     essential, since focus must stay in whatever app the user is actually typing into.
///   - Lets clicks reach the chips without activating the window.
///
/// <para>The bar renders with WPF per-pixel alpha (<c>AllowsTransparency</c>), which is what makes each
/// theme's authored colour and opacity come out exactly as designed and gives true rounded corners. That
/// choice rules out the DWM Mica/Acrylic backdrops, which cannot be applied to a layered window — so the
/// themes provide their own translucency instead of a system blur. Content behind the bar still shows
/// through; it just isn't blurred.</para>
/// </summary>
public static class GlassWindowBehavior
{
    public static void Apply(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
    }

    private static nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_MOUSEACTIVATE)
        {
            // Let the click through to hit-test/click child controls (e.g. a suggestion chip),
            // but tell Windows not to activate/focus this window because of it.
            handled = true;
            return MA_NOACTIVATE;
        }

        return 0;
    }
}
