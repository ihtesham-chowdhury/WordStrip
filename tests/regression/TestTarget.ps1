<#
.SYNOPSIS
    A disposable window containing a real Win32 EDIT control, used as the typing target for regression runs.

.DESCRIPTION
    Verify-PersistentBar.ps1 needs somewhere to type, and it has to be a surface WordStrip actually supports:
    FocusedControlInspector only recognises window classes starting "Edit" or "RichEdit".

    The two obvious targets both fail:
      - Notepad. Windows 11 ships it as a packaged single-instance app, so notepad.exe is a launcher that
        exits immediately and hands back no window handle — and killing a copy the user already had open
        risks their unsaved work.
      - A WinForms TextBox. Its class is "WindowsForms10.EDIT.app.0.<hash>", which does NOT start with
        "Edit", so WordStrip correctly ignores it and no bar ever appears.

    So the control is created directly with CreateWindowEx. Its class is exactly "Edit", which is the real
    thing rather than an approximation of it. The parent is a plain dialog-class window purely so the edit
    control's own text stays its content rather than doubling as a window title.

    Run with -STA. Started by the regression script; not useful on its own.
#>

param(
    # "Edit" is the classic Win32 control. "RichEdit" loads msftedit and creates a RICHEDIT50W instead,
    # which is far closer to what Windows 11 Notepad actually hosts (RichEditD2DPT) and, unlike a plain
    # EDIT, does enough asynchronous work to expose input-ordering races that a plain EDIT hides.
    [ValidateSet('Edit', 'RichEdit')]
    [string] $ControlClass = 'Edit'
)

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class TestTarget {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(int exStyle, string cls, string name, int style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool UpdateWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageW(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("gdi32.dll")]  public static extern IntPtr GetStockObject(int i);

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG {
        public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt;
    }

    public static void Run(string controlClass) {
        const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        const int WS_VISIBLE     = 0x10000000;
        const int WS_CHILD       = 0x40000000;
        const int WS_VSCROLL     = 0x00200000;
        const int WS_TABSTOP     = 0x00010000;
        const int ES_MULTILINE   = 0x0004;
        const int ES_AUTOVSCROLL = 0x0040;
        const int WM_SETFONT     = 0x0030;
        const int DEFAULT_GUI_FONT = 17;
        const int SW_SHOW = 5;

        IntPtr parent = CreateWindowExW(0, "#32770", "WordStrip Regression Target",
            WS_OVERLAPPEDWINDOW | WS_VISIBLE, 220, 220, 780, 340,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (parent == IntPtr.Zero)
            throw new Exception("Could not create the parent window: " + Marshal.GetLastWin32Error());

        // msftedit registers RICHEDIT50W; without loading it CreateWindowEx simply fails.
        if (controlClass == "RichEdit" && LoadLibraryW("msftedit.dll") == IntPtr.Zero)
            throw new Exception("Could not load msftedit.dll: " + Marshal.GetLastWin32Error());

        string className = controlClass == "RichEdit" ? "RICHEDIT50W" : "EDIT";

        // WS_TABSTOP so the dialog class's own focus handling lands on this control when the window is
        // activated, rather than leaving focus on the frame where GetGUIThreadInfo would report no edit.
        IntPtr edit = CreateWindowExW(0, className, "",
            WS_CHILD | WS_VISIBLE | WS_VSCROLL | WS_TABSTOP | ES_MULTILINE | ES_AUTOVSCROLL,
            0, 0, 764, 302, parent, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (edit == IntPtr.Zero)
            throw new Exception("Could not create the " + className + " control: " + Marshal.GetLastWin32Error());

        SendMessageW(edit, WM_SETFONT, GetStockObject(DEFAULT_GUI_FONT), (IntPtr)1);

        ShowWindow(parent, SW_SHOW);
        UpdateWindow(parent);
        SetForegroundWindow(parent);
        SetFocus(edit);

        // No IsDialogMessage in this pump, deliberately: the dialog manager would treat Tab as "move to the
        // next control" and swallow it, and check 3 needs Tab to reach the edit control as a real character.
        MSG msg;
        while (GetMessageW(out msg, IntPtr.Zero, 0, 0) > 0) {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }
    }
}
'@

[TestTarget]::Run($ControlClass)
