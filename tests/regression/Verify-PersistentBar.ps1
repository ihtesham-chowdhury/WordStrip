<#
.SYNOPSIS
    End-to-end check of the persistent suggestion bar against a real Win32 edit control.

.DESCRIPTION
    Bar behaviour cannot be unit-tested: it depends on a system-wide keyboard hook, live focused-control
    inspection, and SendInput reaching another process. This types the way a user would and reads the text
    back with WM_GETTEXT, which is the project's standard for verifying UI behaviour — screenshots are
    useless here (CLAUDE_PROJECT_CONTEXT.md section 12: PrintWindow cannot capture a DWM backdrop).

    Checks:
      1. Typing produces the typed text, and autocorrect fixes an obvious misspelling.
      2. The bar STAYS VISIBLE after a word is committed — the persistent-bar feature itself.
      3. Tab still inserts a tab character between words, i.e. a visible-but-idle bar does not swallow it.
      4. Tab cycles and Space accepts while a word IS in progress.
      5. Esc puts the bar away.

.NOTES
    Takes over the keyboard and the foreground window for roughly half a minute. It types into a throwaway
    window it creates itself, never into the user's apps, and every batch of keystrokes re-verifies that the
    target still has focus before sending — without that, a stray focus change sends the test's typing into
    whatever the user is actually doing (section 12 again).
#>

[CmdletBinding()]
param(
    # Resolved below rather than here: $PSScriptRoot is not reliably populated while param defaults are
    # being evaluated, which silently yields a path rooted at the drive instead of at this folder.
    [string] $ExePath
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $ExePath) {
    $ExePath = Join-Path $here '..\..\src\WordStrip.App\bin\Release\net8.0-windows\WordStrip.exe'
}
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class W {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int SendMessage(IntPtr h, int msg, int w, StringBuilder l);
    [DllImport("user32.dll")] public static extern int SendMessage(IntPtr h, int msg, int w, int l);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO i);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    public delegate bool EnumProc(IntPtr h, IntPtr p);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int l, t, r, b; }
    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO {
        public uint cbSize; public uint flags;
        public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    public static string ClassOf(IntPtr h) {
        var sb = new StringBuilder(256);
        int n = GetClassName(h, sb, sb.Capacity);
        return n > 0 ? sb.ToString(0, n) : "";
    }

    public static string TitleOf(IntPtr h) {
        var sb = new StringBuilder(512);
        int n = GetWindowText(h, sb, sb.Capacity);
        return n > 0 ? sb.ToString(0, n) : "";
    }

    /// <summary>SetForegroundWindow is silently refused from a background process; attaching to the target's
    /// input queue first is the technique that actually works.</summary>
    public static void ForceForeground(IntPtr h) {
        uint ignored;
        uint target = GetWindowThreadProcessId(h, out ignored);
        uint self = GetCurrentThreadId();
        AttachThreadInput(self, target, true);
        SetForegroundWindow(h);
        AttachThreadInput(self, target, false);
    }

    public static IntPtr FocusedControl() {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return IntPtr.Zero;
        uint ignored;
        uint tid = GetWindowThreadProcessId(fg, out ignored);
        var gti = new GUITHREADINFO();
        gti.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
        return GetGUIThreadInfo(tid, ref gti) ? gti.hwndFocus : IntPtr.Zero;
    }

    /// <summary>WM_GETTEXT rather than any managed accessor: the control lives in another process, and this
    /// is one of the few messages USER32 marshals across that boundary for standard controls.</summary>
    /// <summary>Empties the control by message rather than by keystroke. Ctrl+A is not a thing a bare EDIT
    /// control implements — select-all comes from the dialog manager, which this target deliberately does
    /// not run (see TestTarget.ps1, where the pump omits IsDialogMessage so Tab reaches the control).</summary>
    public static void ClearText(IntPtr h) {
        SendMessage(h, 0x00B1 /* EM_SETSEL */, 0, -1);
        SendMessage(h, 0x0303 /* WM_CLEAR  */, 0, 0);
    }

    public static string TextOf(IntPtr h) {
        int len = SendMessage(h, 0x000E /* WM_GETTEXTLENGTH */, 0, 0);
        var sb = new StringBuilder(len + 2);
        SendMessage(h, 0x000D /* WM_GETTEXT */, sb.Capacity, sb);
        return sb.ToString();
    }

    public static IntPtr FindWindowByTitle(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            if (IsWindowVisible(h) && TitleOf(h) == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static IntPtr FirstEditChild(IntPtr parent) {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (h, p) => {
            if (ClassOf(h).StartsWith("Edit", StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Whether the given process has any visible WPF top-level window. The bar is a WPF window and
    /// the settings window is never opened during a run, so for WordStrip this means "the bar is on screen".</summary>
    public static bool HasVisibleWpfWindow(uint pid) {
        bool found = false;
        EnumWindows((h, p) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == pid && IsWindowVisible(h) && ClassOf(h).StartsWith("HwndWrapper")) { found = true; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[void][W]::SetProcessDPIAware()

$script:Failures = @()
function Check([string] $name, [bool] $ok, [string] $detail = '') {
    if ($ok) {
        Write-Host "  PASS  $name" -ForegroundColor Green
    } else {
        Write-Host "  FAIL  $name  $detail" -ForegroundColor Red
        $script:Failures += $name
    }
}

$TargetTitle = 'WordStrip Regression Target'

function Assert-StillFocused([IntPtr] $editHwnd) {
    $focused = [W]::FocusedControl()
    if ($focused -ne $editHwnd) {
        throw "The test window lost focus (focus is on '$([W]::ClassOf($focused))'). Aborting so keystrokes don't land in another app."
    }
}

<#
    One key at a time, with a human-ish gap.

    SendKeys delivers a whole string in microseconds, which no keyboard can do and which the app is not
    built to survive: text replacements are deliberately deferred onto the message loop, so a burst of
    synthetic keys is still draining into the target when the replacement fires and the two interleave
    (CLAUDE_PROJECT_CONTEXT.md sections 6 and 13). Blasting "helo " that way yields "healo". That is the
    harness outrunning a real keyboard, not a defect, so the harness types at a plausible speed instead.
#>
function Send([IntPtr] $editHwnd, [string] $keys, [int] $settleMs = 700, [int] $perKeyMs = 90) {
    Assert-StillFocused $editHwnd

    # One token = one keystroke. Brace groups like {TAB} must stay whole, and the modifier prefixes ^ % +
    # must stay attached to the key they modify — split "^a" and SendKeys types a literal "a".
    $tokens = [regex]::Matches($keys, '[\^%+]*(?:\{[^}]+\}|.)') | ForEach-Object { $_.Value }
    foreach ($token in $tokens) {
        Assert-StillFocused $editHwnd
        [System.Windows.Forms.SendKeys]::SendWait($token)
        Start-Sleep -Milliseconds $perKeyMs
    }

    Start-Sleep -Milliseconds $settleMs
}

Write-Host "`nWordStrip persistent-bar regression" -ForegroundColor Cyan
Write-Host "Executable: $ExePath"

if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath. Build the solution first." }

Get-Process -Name 'WordStrip*' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$app = $null
$target = $null

try {
    # 18 s rather than the ~6 s the index itself takes: the self-contained single-file build extracts itself
    # on first run, so a cold start is appreciably slower than the dev build this was first timed against.
    Write-Host "`nStarting WordStrip and waiting for the SymSpell index..."
    $app = Start-Process -FilePath $ExePath -PassThru
    Start-Sleep -Seconds 18
    if ($app.HasExited) { throw "WordStrip exited immediately (code $($app.ExitCode))." }

    Write-Host "Opening the test window..."
    # The path is quoted because the project lives under "D:\Claude Code": Start-Process joins ArgumentList
    # entries with spaces and quotes nothing, so an unquoted path here is parsed as "-File D:\Claude".
    $targetScript = Join-Path $here 'TestTarget.ps1'
    $target = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-STA', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$targetScript`"" `
        -PassThru

    # Polled rather than slept: a second PowerShell host plus loading System.Windows.Forms lands right around
    # the three seconds a fixed wait would have given it, so a fixed wait fails intermittently.
    $win = [IntPtr]::Zero
    foreach ($attempt in 1..20) {
        Start-Sleep -Milliseconds 500
        $win = [W]::FindWindowByTitle($TargetTitle)
        if ($win -ne [IntPtr]::Zero) { break }
    }
    if ($win -eq [IntPtr]::Zero) { throw "The test window never appeared." }

    [W]::ForceForeground($win)
    Start-Sleep -Milliseconds 800

    $edit = [W]::FocusedControl()
    $editClass = [W]::ClassOf($edit)
    Write-Host "Focused control class: '$editClass'"
    if (-not ($editClass -match '^(Edit|RichEdit)')) {
        throw "Focused control is '$editClass', not a Win32 edit control. Aborting rather than typing blind."
    }

    # --- Warm-up ---------------------------------------------------------------------------------------
    # The very first text replacement after a cold start pays JIT on the whole injection path, and on the
    # self-contained single-file build it can land after the check that reads the text back — which shows up
    # as a half-applied correction ("teh " becoming "tehe " rather than "the "). One throwaway word first
    # makes the run deterministic. The word ends in a space, so WordStrip's own typing buffer is already
    # empty by the time the field is cleared out from under it.
    Write-Host "`nWarming up the injection path..."
    Send $edit 'warm ' 1200
    [W]::ClearText($edit)
    Start-Sleep -Milliseconds 300

    $residue = [W]::TextOf($edit)
    if ($residue -ne '') { throw "Could not clear the test field before starting (contains '$residue')." }

    # --- 1. Typing and autocorrect --------------------------------------------------------------------
    # "teh" and not something like "helo": a correction is only predictable when one candidate wins on both
    # counts. "helo" sits one edit from "help" AND from "hello", so the tie falls to frequency and the answer
    # is "help" — correct behaviour, but a poor thing to assert on. "teh" is one transposition from "the",
    # which then outweighs every other neighbour by three orders of magnitude.
    Write-Host "`n1. Typing and autocorrect"
    Send $edit 'teh '
    $text = [W]::TextOf($edit)
    Check 'autocorrect fixes "teh" to "the"' ($text -eq 'the ') "got '$text'"

    # --- 2. The bar stays up between words ------------------------------------------------------------
    Write-Host "`n2. Persistent bar"
    Check 'bar is visible after the word was committed' ([W]::HasVisibleWpfWindow($app.Id))

    # --- 3. An idle bar must not swallow Tab ----------------------------------------------------------
    # The regression this guards: keying the router off "bar is visible" rather than "bar is completing"
    # means a persistent bar holds Tab hostage in every text field on the system.
    Write-Host "`n3. Tab passes through while the bar is idle"
    $before = [W]::TextOf($edit)
    Send $edit '{TAB}'
    $after = [W]::TextOf($edit)
    Check 'Tab inserts a tab character between words' ($after -eq ($before + "`t")) "before='$before' after='$after'"

    # --- 4. Tab cycles and Space accepts while completing ---------------------------------------------
    Write-Host "`n4. Tab cycles / Space accepts a completion"
    Send $edit 'wor'
    $beforeAccept = [W]::TextOf($edit)
    Send $edit '{TAB}'          # highlight the first candidate
    Send $edit ' '              # accept it
    $afterAccept = [W]::TextOf($edit)
    Check 'accepting a completion replaced the partial word' `
        ($afterAccept -ne $beforeAccept -and $afterAccept -notmatch 'wor$') "before='$beforeAccept' after='$afterAccept'"
    Check 'the accepted word is followed by a space' ($afterAccept.EndsWith(' ')) "got '$($afterAccept -replace "`t", '\t')'"

    # --- 5. Esc dismisses -----------------------------------------------------------------------------
    Write-Host "`n5. Esc dismisses the bar"
    Send $edit '{ESC}' 1200
    Check 'bar is hidden after Esc' (-not [W]::HasVisibleWpfWindow($app.Id))

    Write-Host "`nFinal contents: '$([W]::TextOf($edit) -replace "`t", '\t')'"
}
finally {
    Write-Host "`nCleaning up..."
    if ($target -and -not $target.HasExited) { Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name 'WordStrip*' -ErrorAction SilentlyContinue | Stop-Process -Force
}

if ($script:Failures.Count -gt 0) {
    Write-Host "`n$($script:Failures.Count) check(s) failed: $($script:Failures -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host "`nAll checks passed." -ForegroundColor Green
