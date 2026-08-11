using System.Runtime.InteropServices;

namespace WordStrip.Core.Input;

/// <summary>Raw Win32 P/Invoke declarations. Nothing in here has behavior of its own — see the wrapper classes for that.</summary>
internal static class NativeMethods
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    /// <summary>
    /// A typed character, delivered straight to a control rather than pretended at the keyboard. Both Edit
    /// and RichEdit act on this exactly as they act on real typing, including 0x08 for backspace.
    /// </summary>
    public const int WM_CHAR = 0x0102;

    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_MBUTTONDOWN = 0x0207;

    /// <summary>
    /// Set on KBDLLHOOKSTRUCT.flags when the event came from SendInput rather than real hardware — but this
    /// is a generic "was this synthesized" bit, true for SendInput calls from ANY process (dictation
    /// software, other automation tools, remote-input redirection, etc.), not just ours. Distinguishing our
    /// own injected keystrokes specifically requires the dwExtraInfo marker below, not this flag alone.
    /// </summary>
    public const uint LLKHF_INJECTED = 0x10;

    /// <summary>
    /// Arbitrary sentinel we stamp into KEYBDINPUT.dwExtraInfo on every key we inject ourselves (see
    /// Win32TextInjector), so the hook can tell "WordStrip replacing text" apart from "some other tool
    /// injected real keystrokes that should still be tracked as typing." Real hardware input always has
    /// dwExtraInfo == 0, so an exact match here is effectively unambiguous.
    /// </summary>
    public const nint OwnInjectionMarker = 0x57535452; // ASCII "WSTR"

    /// <summary>Backspace as a character rather than a virtual key. A plain Edit deletes on this; RichEdit ignores it and wants the key.</summary>
    public const char BackspaceCharacter = '\b';

    /// <summary>
    /// Selection messages. Retained for reading the caret in diagnostics; replacement no longer uses them —
    /// see Win32TextInjector for why absolute positions turned out to be unsafe here.
    ///
    /// <para>Backspace turned out to be the one thing the two control types disagree about: a plain EDIT
    /// treats WM_CHAR 0x08 as a delete and ignores nothing, RichEdit ignores the character and wants the
    /// key, and sending the key to an EDIT lost characters outright. Selecting the range and letting the
    /// first typed character overwrite it is what both do identically, because it is what happens when a
    /// person selects a word and types over it.</para>
    ///
    /// <para>Both take plain integers, so they cross a process boundary safely — unlike the richer
    /// selection messages, which pass a pointer only valid in the caller's own address space.</para>
    /// </summary>
    public const int EM_GETSEL = 0x00B0;
    public const int EM_SETSEL = 0x00B1;

    /// <summary>Give up rather than wait on a target that has stopped responding.</summary>
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_PRIOR = 0x21; // Page Up
    public const int VK_NEXT = 0x22;  // Page Down
    public const int VK_HOME = 0x24;
    public const int VK_END = 0x23;
    public const int VK_DELETE = 0x2E;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    public delegate nint LowLevelHookProc(int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelHookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint GetModuleHandle(string? lpModuleName);

    // ---- SendInput / text injection ----

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    /// <summary>
    /// The full INPUT union. MOUSEINPUT and HARDWAREINPUT are declared even though only the keyboard member is
    /// ever used, because they determine the union's size — and therefore sizeof(INPUT), which is passed to
    /// SendInput as cbSize. Declaring only KEYBDINPUT yields a 32-byte INPUT on x64 instead of the required 40,
    /// and SendInput then rejects every call with ERROR_INVALID_PARAMETER and silently injects nothing.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // ---- Focused-control / caret-context inspection ----

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public uint cbSize;
        public uint flags;
        public nint hwndActive;
        public nint hwndFocus;
        public nint hwndCapture;
        public nint hwndMenuOwner;
        public nint hwndMoveSize;
        public nint hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left, top, right, bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(nint hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(nint hWnd, int nIndex);

    public const int GWL_STYLE = -16;
    public const int ES_PASSWORD = 0x0020;

    // ---- Key translation (vkCode -> printable character, honoring layout/shift/capslock) ----

    [DllImport("user32.dll")]
    public static extern nint GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    /// <summary>
    /// Queues a message to a window and returns immediately.
    ///
    /// <para>Posted rather than sent: <c>SendMessage</c> blocks until the target has finished handling it,
    /// which across a process boundary means the app hangs for as long as the other one is busy. Posting
    /// keeps the messages in order — a thread's queue is FIFO — without ever waiting on someone else.</para>
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    /// <summary>Sends a message but refuses to wait forever, so another application being busy cannot hang this one.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SendMessageTimeout(
        nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll")]
    public static extern int ToUnicodeEx(
        uint wVirtKey,
        uint wScanCode,
        byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr, SizeParamIndex = 4)] System.Text.StringBuilder pwszBuff,
        int cchBuff,
        uint wFlags,
        nint dwhkl);
}
