<#
.SYNOPSIS
    Measures whether the suggestion bar holds still while someone types.

.DESCRIPTION
    Types a sentence into a real Win32 edit control at a chosen speed and, after every keystroke, reads the
    bar window's rectangle straight from USER32. Reports how many distinct sizes and positions the bar had
    while visible, plus the app's own frame-probe counters (renders, coalesced updates, window resizes,
    window moves) from WORDSTRIP_FRAMELOG.

    The acceptance bar for the visual layer is stated in these terms: while typing, the bar's content
    changes and nothing else does. With the default fixed geometry and bottom-centre placement that means
    exactly one size and one position for the whole sentence.

    Same safety rules as Verify-PersistentBar.ps1: it types only into a throwaway window it creates itself,
    re-checks focus before every key, and points the app at a temporary data folder so the user's settings
    and vocabulary are never read or written.

.NOTES
    Keep non-ASCII out of string literals in this file; see the note in Verify-PersistentBar.ps1.
#>

[CmdletBinding()]
param(
    [string] $ExePath,
    [ValidateSet('Edit', 'RichEdit')]
    [string] $ControlClass = 'Edit',
    [int] $PerKeyMs = 40,
    [string] $Sentence = 'I am looking forward to the new project and the work we will do together ',

    # Palette for the run. A typed switch rather than a JSON string on purpose: Windows PowerShell strips
    # embedded double quotes from arguments passed to a child "powershell -File", so JSON handed in that way
    # arrived unparseable and the app silently ran on defaults - which made one measurement lie.
    [ValidateSet('Auto', 'Light', 'Dark')]
    [string] $Appearance = 'Auto'
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
using System.Runtime.InteropServices;

public static class M {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool GetGUIThreadInfo(uint tid, ref GUITHREADINFO i);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
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

    public static IntPtr FindWindowByTitle(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, p) => {
            if (IsWindowVisible(h) && TitleOf(h) == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>The bar's rectangle as "left,top,width,height", or "" while it is not on screen.</summary>
    public static string BarRect(uint pid) {
        string found = "";
        EnumWindows((h, p) => {
            uint owner;
            GetWindowThreadProcessId(h, out owner);
            if (owner == pid && IsWindowVisible(h) && ClassOf(h).StartsWith("HwndWrapper")) {
                RECT r;
                if (GetWindowRect(h, out r)) found = r.l + "," + r.t + "," + (r.r - r.l) + "," + (r.b - r.t);
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@

[void][M]::SetProcessDPIAware()

Write-Host "`nWordStrip bar stability measurement" -ForegroundColor Cyan
Write-Host "Target control: $ControlClass, $PerKeyMs ms between keys"
if (-not (Test-Path $ExePath)) { throw "Not found: $ExePath. Build the solution first." }

Get-Process -Name 'WordStrip*' -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$dataDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("wordstrip-stability-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $dataDirectory | Out-Null
$appearanceValue = @{ Auto = 0; Light = 1; Dark = 2 }[$Appearance]
[System.IO.File]::WriteAllText(
    (Join-Path $dataDirectory 'settings.json'),
    "{ `"AppearanceMode`": $appearanceValue }",
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Appearance: $Appearance"
$frameLog = Join-Path ([System.IO.Path]::GetTempPath()) 'wordstrip_frames.log'
Remove-Item $frameLog -ErrorAction SilentlyContinue

$env:WORDSTRIP_DATA_DIR = $dataDirectory
$env:WORDSTRIP_FRAMELOG = '1'

$app = $null
$target = $null
$failed = $false

try {
    $app = Start-Process -FilePath $ExePath -PassThru
    Start-Sleep -Seconds 18
    if ($app.HasExited) { throw "WordStrip exited immediately (code $($app.ExitCode))." }

    $targetScript = Join-Path $here 'TestTarget.ps1'
    $target = Start-Process -FilePath 'powershell.exe' `
        -ArgumentList '-STA', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$targetScript`"", '-ControlClass', $ControlClass `
        -PassThru

    $win = [IntPtr]::Zero
    foreach ($attempt in 1..20) {
        Start-Sleep -Milliseconds 500
        $win = [M]::FindWindowByTitle('WordStrip Regression Target')
        if ($win -ne [IntPtr]::Zero) { break }
    }
    if ($win -eq [IntPtr]::Zero) { throw "The test window never appeared." }

    $edit = [IntPtr]::Zero
    foreach ($attempt in 1..10) {
        [M]::ForceForeground($win)
        Start-Sleep -Milliseconds 500
        $edit = [M]::FocusedControl()
        if ([M]::ClassOf($edit) -match '^(Edit|RichEdit|RICHEDIT)') { break }
    }
    if (-not ([M]::ClassOf($edit) -match '^(Edit|RichEdit|RICHEDIT)')) { throw "Could not focus the test control." }

    # Warm up so the first sample is not JIT.
    [System.Windows.Forms.SendKeys]::SendWait('warm ')
    Start-Sleep -Milliseconds 1500

    $samples = New-Object System.Collections.Generic.List[string]
    foreach ($ch in $Sentence.ToCharArray()) {
        if ([M]::FocusedControl() -ne $edit) { throw "Focus left the test control. Aborting." }
        $key = if ($ch -eq ' ') { ' ' } elseif ('+^%~(){}[]'.Contains($ch)) { '{' + $ch + '}' } else { [string]$ch }
        [System.Windows.Forms.SendKeys]::SendWait($key)
        Start-Sleep -Milliseconds $PerKeyMs
        $rect = [M]::BarRect([uint32]$app.Id)
        if ($rect) { $samples.Add($rect) }
    }
    Start-Sleep -Milliseconds 2500   # let the frame probe's sampling window close and write its line

    # Rapid Tabs: each one replaces the prediction and moves the lens. The frame probe samples the lens
    # motion ("tab-cycle", or "tab-repeat" once presses come faster than a single spring can settle), which
    # is what shows whether repeated Tab stays continuous or makes the lens lag.
    foreach ($i in 1..6) {
        if ([M]::FocusedControl() -ne $edit) { throw "Focus left the test control. Aborting." }
        [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
        Start-Sleep -Milliseconds 110
    }
    Start-Sleep -Milliseconds 1500

    $sizes = $samples | ForEach-Object { ($_ -split ',')[2..3] -join 'x' } | Sort-Object -Unique
    $positions = $samples | ForEach-Object { ($_ -split ',')[0..1] -join ',' } | Sort-Object -Unique

    Write-Host ""
    Write-Host "Keystrokes sampled with the bar visible: $($samples.Count) of $($Sentence.Length)"
    Write-Host "Distinct bar sizes:     $($sizes.Count)  ($($sizes -join '; '))"
    Write-Host "Distinct bar positions: $($positions.Count)  ($($positions -join '; '))"

    if (Test-Path $frameLog) {
        Write-Host "`nFrame probe (WORDSTRIP_FRAMELOG):"
        Get-Content $frameLog | ForEach-Object { Write-Host "  $_" }
    }

    if ($sizes.Count -ne 1) { Write-Host "  FAIL  the bar changed size while typing" -ForegroundColor Red; $failed = $true }
    else { Write-Host "  PASS  the bar kept one size for the whole sentence" -ForegroundColor Green }

    if ($positions.Count -ne 1) { Write-Host "  FAIL  the bar moved while typing" -ForegroundColor Red; $failed = $true }
    else { Write-Host "  PASS  the bar never moved" -ForegroundColor Green }
}
finally {
    if ($target -and -not $target.HasExited) { Stop-Process -Id $target.Id -Force -ErrorAction SilentlyContinue }
    Get-Process -Name 'WordStrip*' -ErrorAction SilentlyContinue | Stop-Process -Force
    $env:WORDSTRIP_DATA_DIR = $null
    $env:WORDSTRIP_FRAMELOG = $null
    if ($dataDirectory -and (Test-Path $dataDirectory)) { Remove-Item $dataDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}

if ($failed) { exit 1 }
