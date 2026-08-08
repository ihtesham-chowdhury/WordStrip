<#
    Builds everything shareable in one go:

      publish\portable\WordStrip.exe        self-contained, no .NET install needed on the target machine
      publish\WordStrip-Setup-<ver>.exe     installer with Start Menu entries and an uninstaller

    Usage:  .\build-release.ps1
#>

$ErrorActionPreference = "Stop"

$root       = $PSScriptRoot
$project    = Join-Path $root "src\WordStrip.App\WordStrip.App.csproj"
$publishDir = Join-Path $root "publish"
$portable   = Join-Path $publishDir "portable"
$issScript  = Join-Path $root "installer\WordStrip.iss"

Write-Host "==> Cleaning previous output" -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $portable -Force | Out-Null

# Self-contained + single-file so testers need nothing preinstalled. Trimming is deliberately OFF: WPF and
# the reflection the XAML binding system relies on do not survive the trimmer reliably.
Write-Host "==> Publishing self-contained x64 build" -ForegroundColor Cyan
& dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $portable

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $portable "WordStrip.exe"
if (-not (Test-Path $exe)) { throw "Expected published exe at $exe" }
$exeSizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "    portable exe: $exe ($exeSizeMb MB)" -ForegroundColor Green

# The dictionary is embedded in the assembly, so the portable output must be exactly one file. Anything
# else here means content leaked out of the bundle and testers could break the app by copying only the exe.
$loose = Get-ChildItem $portable -Recurse -File | Where-Object { $_.Name -ne "WordStrip.exe" }
if ($loose) {
    Write-Warning "Unexpected extra files in the portable output:"
    $loose | ForEach-Object { Write-Warning "    $($_.FullName)" }
}

Write-Host "==> Building installer" -ForegroundColor Cyan
$iscc = Get-Command iscc.exe -ErrorAction SilentlyContinue
if (-not $iscc) {
    foreach ($candidate in @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
        if (Test-Path $candidate) { $iscc = $candidate; break }
    }
} else {
    $iscc = $iscc.Source
}

if (-not $iscc) {
    Write-Warning "Inno Setup (ISCC.exe) not found - skipping installer. Portable exe is still in $portable."
} else {
    & $iscc $issScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    Get-ChildItem $publishDir -Filter "WordStrip-Setup-*.exe" | ForEach-Object {
        $mb = [math]::Round($_.Length / 1MB, 1)
        Write-Host "    installer: $($_.FullName) ($mb MB)" -ForegroundColor Green
    }
}

# Lives in installer\ rather than publish\ so the clean step at the top of this script can't delete it.
Copy-Item (Join-Path $root "installer\READ-ME-FIRST.txt") $publishDir -Force
Write-Host "    tester notes: $(Join-Path $publishDir 'READ-ME-FIRST.txt')" -ForegroundColor Green

Write-Host "==> Done" -ForegroundColor Cyan
