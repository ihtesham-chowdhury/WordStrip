using System.Runtime.InteropServices;
using static WordStrip.Core.Input.NativeMethods;
using POINT = WordStrip.Core.Input.NativeMethods.POINT;

namespace WordStrip.Core.Input;

/// <summary>
/// Thin wrapper around a WH_MOUSE_LL hook. We only care about button-down events — a click anywhere
/// means the caret may have moved somewhere our tracked "current word" buffer no longer matches, so
/// callers use this purely as a signal to reset that buffer rather than to read text.
/// </summary>
public sealed class LowLevelMouseHook : IDisposable
{
    private readonly LowLevelHookProc _proc;
    private nint _hookHandle;

    public event EventHandler? MouseButtonDown;

    public LowLevelMouseHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != 0) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hModule = GetModuleHandle(curModule?.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, hModule, 0);
        if (_hookHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to install low-level mouse hook (Win32 error {error}).");
        }
    }

    public void Uninstall()
    {
        if (_hookHandle == 0) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = 0;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0 && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN || wParam == WM_MBUTTONDOWN))
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            if (!IsOverOwnWindow(data.pt))
                MouseButtonDown?.Invoke(this, EventArgs.Empty);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    /// <summary>
    /// Clicks landing on our own windows must not reset the typing buffer. Clicking a suggestion is the one
    /// case where the click needs the buffer that a reset would destroy: the handler reads the in-progress
    /// word to know what to replace, and the mouse hook runs first, so without this the word is already gone
    /// by the time the chip's Click fires and nothing gets inserted.
    /// </summary>
    private static bool IsOverOwnWindow(POINT point)
    {
        var hwnd = WindowFromPoint(point);
        if (hwnd == 0) return false;

        GetWindowThreadProcessId(hwnd, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    public void Dispose() => Uninstall();
}
