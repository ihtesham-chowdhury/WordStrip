using System.Runtime.InteropServices;
using static WordStrip.Core.Input.NativeMethods;

namespace WordStrip.Core.Input;

public sealed class KeyEventArgs : EventArgs
{
    public required int VirtualKeyCode { get; init; }
    public required uint ScanCode { get; init; }
    /// <summary>
    /// True only when this event came from our own <see cref="Win32TextInjector"/> specifically — matched via
    /// a private dwExtraInfo marker, not the generic "was this SendInput" LLKHF_INJECTED flag. Real hardware
    /// keystrokes AND SendInput events from other tools (dictation software, other automation, remote-input
    /// redirection) are both treated as real typing here; only our own text replacements are filtered out.
    /// </summary>
    public required bool IsInjected { get; init; }
    /// <summary>Set true by a handler to prevent the keystroke from reaching the focused application.</summary>
    public bool Suppress { get; set; }
}

/// <summary>
/// Thin wrapper around a WH_KEYBOARD_LL hook. Raises KeyDown/KeyUp for every keystroke system-wide,
/// including a Suppress flag so a handler can swallow a key (e.g. Tab, while the suggestion bar owns it)
/// before it reaches the focused application.
/// </summary>
public sealed class LowLevelKeyboardHook : IDisposable
{
    private readonly LowLevelHookProc _proc; // must keep a live reference — GC would otherwise collect the delegate out from under the native callback
    private nint _hookHandle;

    public event EventHandler<KeyEventArgs>? KeyDown;
    public event EventHandler<KeyEventArgs>? KeyUp;

    public LowLevelKeyboardHook()
    {
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hookHandle != 0) return;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        var hModule = GetModuleHandle(curModule?.ModuleName);

        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hModule, 0);
        if (_hookHandle == 0)
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"Failed to install low-level keyboard hook (Win32 error {error}).");
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
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN || wParam == WM_KEYUP || wParam == WM_SYSKEYUP))
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var args = new KeyEventArgs
            {
                VirtualKeyCode = (int)data.vkCode,
                ScanCode = data.scanCode,
                IsInjected = data.dwExtraInfo == OwnInjectionMarker,
            };

            if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
                KeyDown?.Invoke(this, args);
            else
                KeyUp?.Invoke(this, args);

            if (args.Suppress)
                return 1; // non-zero return from an LL hook swallows the keystroke
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
