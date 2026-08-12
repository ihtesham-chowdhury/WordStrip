<#
.SYNOPSIS
    Reports which running applications have actually loaded the WordStrip text service.

.DESCRIPTION
    This is the Stage 1 verification, and the raw material for the compatibility matrix the phase brief
    requires. Registration proves nothing about loading: a DLL can be registered perfectly and still fail to
    load into a host because of an architecture mismatch, a missing runtime, or a host that does not use TSF
    at all. All three present identically - nothing happens, and nothing is reported anywhere.

    Two independent sources are checked, because they answer different questions:

      * the module list of every running process, which is ground truth about what is loaded right now
      * the load log the DLL writes, which also captures hosts that have since exited

    Needs no elevation to read module lists of processes owned by this user. Processes owned by other users
    or running elevated will be silently skipped, which is itself worth knowing when testing elevated apps.

.NOTES
    Keep non-ASCII out of string literals here - see CLAUDE_PROJECT_CONTEXT.md section 12.
#>

[CmdletBinding()]
param(
    # Also print the raw log rather than just the summary.
    [switch] $Full
)

$ErrorActionPreference = 'Stop'

Write-Host ""
Write-Host "=== PROCESSES WITH WordStripTip.dll LOADED RIGHT NOW ===" -ForegroundColor Cyan

$found = @()
$skipped = 0

foreach ($p in Get-Process) {
    try {
        foreach ($m in $p.Modules) {
            if ($m.ModuleName -like "WordStripTip*") {
                $found += [pscustomobject]@{ Process = $p.ProcessName; Pid = $p.Id; Path = $m.FileName }
                break
            }
        }
    } catch {
        # Access denied: another user's process, or elevated while we are not.
        $skipped++
    }
}

if ($found.Count -gt 0) {
    $found | Sort-Object Process | Format-Table -AutoSize
} else {
    Write-Host "  none" -ForegroundColor Yellow
    Write-Host "  If the service is registered, it still only loads once selected as an input method."
    Write-Host "  Win+Space, or Settings > Time & Language > Typing > Advanced keyboard settings."
}

Write-Host ("  ({0} processes could not be inspected - other user or elevated)" -f $skipped) -ForegroundColor DarkGray

Write-Host ""
Write-Host "=== LOAD LOG (includes hosts that have since exited) ===" -ForegroundColor Cyan

$log = Join-Path $env:LOCALAPPDATA "WordStrip\tip-load.log"
if (-not (Test-Path $log)) {
    Write-Host "  no log yet at $log" -ForegroundColor Yellow
    Write-Host "  Meaning: the DLL has never been instantiated by any host."
    return
}

$lines = Get-Content $log
Write-Host ("  {0} entries, last written {1}" -f $lines.Count, (Get-Item $log).LastWriteTime)
Write-Host ""

# host=<exe> is what matters; the rest is timing detail.
$byHost = $lines |
    ForEach-Object { if ($_ -match 'host=(\S+)\s') { $matches[1] } } |
    Group-Object |
    Sort-Object Count -Descending

Write-Host "  hosts that loaded it:"
foreach ($h in $byHost) { Write-Host ("    {0,-28} {1} events" -f $h.Name, $h.Count) }

$activated = ($lines | Where-Object { $_ -match 'ACTIVATE' }).Count
$created   = ($lines | Where-Object { $_ -match 'CREATE' }).Count
Write-Host ""
Write-Host ("  CREATE events   : {0}" -f $created)
Write-Host ("  ACTIVATE events : {0}" -f $activated)
if ($created -gt 0 -and $activated -eq 0) {
    Write-Host "  CREATE without ACTIVATE means TSF instantiated the service and then rejected it." -ForegroundColor Yellow
}

if ($Full) {
    Write-Host ""
    Write-Host "=== RAW LOG ===" -ForegroundColor Cyan
    $lines | ForEach-Object { "  $_" }
}
