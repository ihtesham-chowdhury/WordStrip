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
$tipDir     = Join-Path $root "src\WordStrip.Tip"

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

# The native Text Services Framework component (Chrome/Edge/Word support) needs a C++ toolchain the managed
# build does not - so this is a soft skip, not a failure, exactly like Inno Setup being optional below.
# Anyone without Visual Studio Build Tools still gets a complete, working build; it just falls back to the
# keyboard-hook path everywhere, same as today.
Write-Host "==> Building the native text service (optional)" -ForegroundColor Cyan
$tipBuildBat = Join-Path $tipDir "build.bat"

# Piping a native command's stderr through PowerShell (2>&1) wraps each line as a terminating ErrorRecord
# under $ErrorActionPreference = "Stop" above - regardless of the process's actual exit code. cmd.exe's own
# redirection avoids that entirely: it never enters PowerShell's error stream, so cosmetic stderr noise from
# a successful build can never be mistaken for a failure of this script.
$tipLog = Join-Path $env:TEMP "wordstrip-tip-build.log"
cmd.exe /c "`"$tipBuildBat`" Release > `"$tipLog`" 2>&1"
$tipBuildExit = $LASTEXITCODE
if (Test-Path $tipLog) { Get-Content $tipLog | ForEach-Object { Write-Host "    $_" } }

$tipDll = Join-Path $tipDir "bin\x64\Release\WordStripTip.dll"
if ($tipBuildExit -ne 0) {
    Write-Warning "Native text service build exited with code $tipBuildExit (see log above)."
}

if (Test-Path $tipDll) {
    Copy-Item $tipDll $portable -Force
    $tipSizeKb = [math]::Round((Get-Item $tipDll).Length / 1KB, 0)
    Write-Host "    WordStripTip.dll: $tipSizeKb KB (browser and Office support included)" -ForegroundColor Green
} else {
    Write-Warning "WordStripTip.dll was not built (no C++ toolchain?) - continuing without browser/Office support."
}

# The ONNX Runtime NuGet package leaks its native import libraries (.lib) into every publish output that
# references it, even though nothing at runtime ever needs them - they exist only for a C++ project linking
# against onnxruntime directly, which this is not. Harmless to ship and pointless to keep; removed rather
# than merely warned about, since the check below exists precisely to catch content nobody meant to be here.
Get-ChildItem $portable -Filter "*.lib" -ErrorAction SilentlyContinue | Remove-Item -Force

# Everything the dictionary and n-gram model need is embedded in the exe, so the portable output should hold
# only that exe plus, when the native build succeeded, the one text-service DLL beside it. Anything else here
# means content leaked out of the bundle and testers could break the app by copying only part of it.
$expected = @("WordStrip.exe", "WordStripTip.dll")
$loose = Get-ChildItem $portable -Recurse -File | Where-Object { $expected -notcontains $_.Name }
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
