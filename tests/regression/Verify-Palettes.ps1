<#
.SYNOPSIS
    Checks that the Settings window's three palettes parse and define exactly the same keys.

.DESCRIPTION
    The window loads one of Palette.Light, Palette.Dark or Palette.HighContrast depending on the system,
    and every control style reads its colours from whichever is loaded. A key present in one palette and
    missing from another is therefore invisible until someone runs Windows in that mode: the control either
    throws as the window opens, or silently draws with no brush at all.

    Only two of the three can be seen on any one machine without changing the user's system settings, which
    is exactly why this exists. It parses all three the way WPF does and compares their key sets.

    Runs in a second and touches nothing: no app is started and no setting is read or written.

.NOTES
    Needs -STA, which is how XamlReader must be called.
#>

[CmdletBinding()]
param(
    [string] $DesignDirectory
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $DesignDirectory) {
    $DesignDirectory = Join-Path $here '..\..\src\WordStrip.App\UI\Design'
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase

Write-Host "`nWordStrip palette check" -ForegroundColor Cyan

$names = @('Palette.Light.xaml', 'Palette.Dark.xaml', 'Palette.HighContrast.xaml')
$loaded = @{}
$failed = $false

foreach ($name in $names) {
    $path = Join-Path $DesignDirectory $name
    if (-not (Test-Path $path)) { throw "Not found: $path" }

    $stream = [System.IO.File]::OpenRead($path)
    try {
        $dictionary = [System.Windows.Markup.XamlReader]::Load($stream)
        $loaded[$name] = @($dictionary.Keys | ForEach-Object { [string]$_ } | Sort-Object)
        Write-Host ("  PASS  {0} parses ({1} keys)" -f $name, $loaded[$name].Count) -ForegroundColor Green
    }
    catch {
        Write-Host ("  FAIL  {0} does not parse: {1}" -f $name, $_.Exception.Message) -ForegroundColor Red
        $failed = $true
    }
    finally { $stream.Dispose() }
}

if (-not $failed) {
    $reference = $loaded['Palette.Light.xaml']

    foreach ($name in $names | Where-Object { $_ -ne 'Palette.Light.xaml' }) {
        $difference = Compare-Object $reference $loaded[$name]
        if ($difference) {
            $failed = $true
            Write-Host ("  FAIL  {0} does not match Palette.Light.xaml" -f $name) -ForegroundColor Red
            foreach ($item in $difference) {
                $where = if ($item.SideIndicator -eq '<=') { 'missing from ' + $name } else { 'only in ' + $name }
                Write-Host ("          {0}: {1}" -f $item.InputObject, $where) -ForegroundColor Red
            }
        }
        else {
            Write-Host ("  PASS  {0} defines the same keys" -f $name) -ForegroundColor Green
        }
    }
}

if ($failed) { Write-Host "`nPalette check failed." -ForegroundColor Red; exit 1 }

Write-Host "`nAll palettes agree." -ForegroundColor Green
