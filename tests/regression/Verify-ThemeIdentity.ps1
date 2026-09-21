<#
.SYNOPSIS
    Checks that no two themes have drifted into being the same theme.

.NOTES
    Keep non-ASCII out of string literals in this file; Windows PowerShell reads a BOM-less script as ANSI
    and an em dash becomes two bytes that break the parser. The same note is on Verify-PersistentBar.ps1.

.DESCRIPTION
    Six themes are only worth having if they are different from each other. The rule this enforces is the
    one the design brief sets: every pair must differ in at least three of the dimensions a theme has, and
    must still be told apart with the colour taken away - which means differing in something other than hue.

    The dimensions compared, straight from the catalogue's own tokens:

      surface lightness   how light or dark the material is (this one survives grayscale)
      surface opacity     translucent against opaque
      selection language  tonal, capsule, raised, filled, underline, block
      radius factor       the silhouette
      density bias        how tall it sits over the same text
      rhythm factor       how far apart the candidates are
      dividers            ruled against spaced
      typeface            proportional against monospaced
      motion              how quickly it settles
      blur                how much of the backdrop comes through

    This reads the catalogue source rather than running the app, so it is fast, needs no display, and fails
    the moment two themes are edited towards each other.
#>

[CmdletBinding()]
param(
    [string] $CatalogPath
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $CatalogPath) {
    $CatalogPath = Join-Path $here '..\..\src\WordStrip.App\UI\Theming\ThemeCatalog.cs'
}
if (-not (Test-Path $CatalogPath)) { throw "Not found: $CatalogPath" }

$source = Get-Content $CatalogPath -Raw

Write-Host "`nWordStrip theme identity check" -ForegroundColor Cyan

# Each theme's block runs from its factory method to the closing of the record.
$themes = @()
$blocks = [regex]::Matches($source, '(?s)private static ThemeDefinition (\w+)\(\) => new\(\)\s*\{(.*?)\n    \};')

foreach ($block in $blocks) {
    $name = $block.Groups[1].Value
    $body = $block.Groups[2].Value

    function Value([string] $pattern, [string] $text) {
        $match = [regex]::Match($text, $pattern)
        if (-not $match.Success) { throw "$name is missing $pattern" }
        return $match.Groups[1].Value
    }

    $lightMatch = [regex]::Match($body, '(?s)OverLight = new ThemeVariant\s*\{(.*?)\n        \},')
    $light = $lightMatch.Groups[1].Value

    $surface = [regex]::Match($light, 'Surface = Rgb\(0x(\w\w), 0x(\w\w), 0x(\w\w)\), SurfaceOpacity = ([\d.]+)')
    $r = [Convert]::ToInt32($surface.Groups[1].Value, 16)
    $g = [Convert]::ToInt32($surface.Groups[2].Value, 16)
    $b = [Convert]::ToInt32($surface.Groups[3].Value, 16)

    $themes += [pscustomobject]@{
        Name       = $name
        Lightness  = [Math]::Round((0.299 * $r + 0.587 * $g + 0.114 * $b) / 255, 3)
        Opacity    = [double](Value 'SurfaceOpacity = ([\d.]+)' $light)
        Selection  = Value 'Selection = SelectionStyle\.(\w+)' $body
        Radius     = [double](Value 'RadiusFactor = ([\d.]+)' $body)
        Density    = [double](Value 'DensityBias = ([\d.]+)' $body)
        Rhythm     = [double](Value 'RhythmFactor = ([\d.]+)' $body)
        Dividers   = [double](Value 'DividerStrength = ([\d.]+)' $body)
        Typeface   = (Value 'FontFamily = ([^,\r\n]+)' $body)
        Motion     = [double](Value 'MotionFactor = ([\d.]+)' $body)
        Blur       = Value 'Blur = BackdropBlur\.(\w+)' $body
    }
}

Write-Host ("Themes found: {0}" -f $themes.Count)
if ($themes.Count -ne 6) { Write-Host "  FAIL  expected six themes" -ForegroundColor Red; exit 1 }

$themes | Format-Table Name, Lightness, Opacity, Selection, Radius, Density, Rhythm, Dividers, Motion, Blur -AutoSize |
    Out-String | Write-Host

$failed = $false

for ($i = 0; $i -lt $themes.Count; $i++) {
    for ($j = $i + 1; $j -lt $themes.Count; $j++) {
        $a = $themes[$i]
        $b = $themes[$j]

        $differences = @()
        if ([Math]::Abs($a.Lightness - $b.Lightness) -ge 0.12) { $differences += 'lightness' }
        if ([Math]::Abs($a.Opacity - $b.Opacity) -ge 0.10) { $differences += 'opacity' }
        if ($a.Selection -ne $b.Selection) { $differences += 'selection' }
        if ([Math]::Abs($a.Radius - $b.Radius) -ge 0.15) { $differences += 'radius' }
        if ([Math]::Abs($a.Density - $b.Density) -ge 0.06) { $differences += 'density' }
        if ([Math]::Abs($a.Rhythm - $b.Rhythm) -ge 0.15) { $differences += 'rhythm' }
        if ([Math]::Abs($a.Dividers - $b.Dividers) -ge 0.2) { $differences += 'dividers' }
        if ($a.Typeface -ne $b.Typeface) { $differences += 'typeface' }
        if ([Math]::Abs($a.Motion - $b.Motion) -ge 0.25) { $differences += 'motion' }
        if ($a.Blur -ne $b.Blur) { $differences += 'blur' }

        # Colour is the one difference that does not survive a grayscale print, a colour-blind user or a
        # low-quality projector. Every pair has to differ in something structural as well.
        $structural = $differences | Where-Object { $_ -in @('selection', 'radius', 'density', 'rhythm', 'dividers', 'typeface', 'lightness', 'opacity') }

        $pair = "{0} vs {1}" -f $a.Name, $b.Name

        if ($differences.Count -lt 3) {
            Write-Host ("  FAIL  {0}: only {1} difference(s) - {2}" -f $pair, $differences.Count, ($differences -join ', ')) -ForegroundColor Red
            $failed = $true
        }
        elseif ($structural.Count -lt 2) {
            Write-Host ("  FAIL  {0}: differs mostly by colour - {1}" -f $pair, ($differences -join ', ')) -ForegroundColor Red
            $failed = $true
        }
        else {
            Write-Host ("  PASS  {0}: {1}" -f $pair, ($differences -join ', ')) -ForegroundColor Green
        }
    }
}

if ($failed) { Write-Host "`nTwo or more themes are too alike." -ForegroundColor Red; exit 1 }

Write-Host "`nAll fifteen pairs are distinguishable." -ForegroundColor Green
