<#
.SYNOPSIS
    Downloads the source text the n-gram model is built from, into .corpus\ (gitignored).

.DESCRIPTION
    Two sources, both redistributable, and both credited in README.md:

      - SymSpell's frequency_bigramdictionary_en_243_342.txt (MIT). Curated bigram counts derived from
        Google Books, from the same project as the unigram dictionary WordStrip already bundles. Bigrams
        only, which is why it cannot be the sole source for a phase whose headline feature is trigrams.

      - Project Gutenberg public-domain books. These supply the trigrams, and a second opinion on bigrams.
        Downloaded politely: one at a time, with a pause between, skipping anything already fetched.

    Neither is committed. The repo carries only the generated model under assets\ngram\, so regenerating
    means re-running this and then Build-NGramModel.

    Gutenberg's own licence header and footer are stripped here rather than in the builder: the boilerplate
    is identical across every book, so leaving it in would make phrases like "project gutenberg literary
    archive foundation" some of the most confident predictions in the model.

.EXAMPLE
    powershell -File tools\ngram\Fetch-Corpus.ps1
#>

[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $here '..\..')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot '.corpus' }

$gutenbergDir = Join-Path $OutputDirectory 'gutenberg'
New-Item -ItemType Directory -Force -Path $gutenbergDir | Out-Null

$userAgent = 'WordStrip-corpus-builder/1.0 (local n-gram model build; contact via project repo)'

# --- SymSpell bigrams ---------------------------------------------------------------------------------

$symSpellPath = Join-Path $OutputDirectory 'symspell_bigrams.txt'
if ($Force -or -not (Test-Path $symSpellPath)) {
    Write-Host "Fetching SymSpell bigram dictionary..." -ForegroundColor Cyan
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -UserAgent $userAgent `
        -Uri 'https://raw.githubusercontent.com/wolfgarbe/SymSpell/master/SymSpell/frequency_bigramdictionary_en_243_342.txt' `
        -OutFile $symSpellPath
} else {
    Write-Host "SymSpell bigram dictionary already present." -ForegroundColor DarkGray
}
Write-Host ("  {0:N2} MB" -f ((Get-Item $symSpellPath).Length / 1MB)) -ForegroundColor Green

# --- Project Gutenberg --------------------------------------------------------------------------------

<#
    Chosen for prose that resembles how people write sentences, not for literary merit. Dialogue-heavy
    novels are over-represented on purpose — Doyle and Christie especially — because the model's job is to
    predict the next word someone is typing, and conversational prose is closer to that than exposition is.

    Deliberately excluded: Shakespeare and other early-modern English (thee/thou/hath skews the counts
    badly), and Ulysses (its prose is wonderful and statistically useless).
#>
$bookIds = @(
    1342, 84, 11, 12, 1661, 244, 2097, 108, 834,     # Austen, Shelley, Carroll, Doyle
    863, 1155,                                        # Christie
    2701, 98, 1400, 730, 766, 580, 46,                # Melville, Dickens
    174, 345, 16, 219, 120, 36, 35, 43, 5230, 159,    # Wilde, Stoker, Barrie, Conrad, Stevenson, Wells
    1260, 768, 158, 161, 141, 105,                    # Brontes, more Austen
    2814, 74, 76,                                     # Joyce (Dubliners), Twain
    1399, 2554, 600, 2600,                            # Russian novels in translation
    64317, 541, 209,                                  # Fitzgerald, Wharton, James
    514, 271, 55, 236, 113, 146, 289,                 # Alcott, Sewell, Baum, Kipling, Burnett, Grahame
    215, 910,                                         # London
    1184, 135, 996,                                   # Dumas, Hugo, Cervantes
    205, 2680, 1232, 5200, 1952, 33                   # Thoreau, Aurelius, Machiavelli, Kafka, Gilman, Hawthorne
)

Write-Host "`nFetching $($bookIds.Count) Project Gutenberg texts..." -ForegroundColor Cyan

$fetched = 0
$skipped = 0
$failed = @()

foreach ($id in $bookIds) {
    $path = Join-Path $gutenbergDir "$id.txt"
    if ((Test-Path $path) -and -not $Force) { $skipped++; continue }

    try {
        $raw = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 120 -UserAgent $userAgent `
                    -Uri "https://www.gutenberg.org/cache/epub/$id/pg$id.txt").Content

        # Keep only what sits between the licence markers. Without this the header and footer — which are
        # word-for-word identical in every book — would be the single most repeated text in the corpus.
        $startMatch = [regex]::Match($raw, '\*\*\*\s*START OF (THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*')
        $endMatch   = [regex]::Match($raw, '\*\*\*\s*END OF (THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*')

        if ($startMatch.Success -and $endMatch.Success -and $endMatch.Index -gt $startMatch.Index) {
            $from = $startMatch.Index + $startMatch.Length
            $body = $raw.Substring($from, $endMatch.Index - $from)
        } else {
            # Better to skip than to fold a few thousand words of licence text into the counts.
            $failed += "$id (licence markers not found)"
            continue
        }

        [System.IO.File]::WriteAllText($path, $body, [System.Text.UTF8Encoding]::new($false))
        $fetched++
        Write-Host ("  {0,-6} {1,7:N0} KB" -f $id, ($body.Length / 1KB)) -ForegroundColor DarkGray

        Start-Sleep -Milliseconds 700   # be a considerate client; this is someone else's free service
    }
    catch {
        $failed += "$id ($($_.Exception.Message))"
    }
}

$totalMb = ((Get-ChildItem $gutenbergDir -Filter *.txt | Measure-Object -Property Length -Sum).Sum) / 1MB

Write-Host "`nFetched $fetched, already present $skipped, failed $($failed.Count)." -ForegroundColor Cyan
if ($failed.Count -gt 0) {
    Write-Warning "Could not fetch:"
    $failed | ForEach-Object { Write-Warning "    $_" }
}
Write-Host ("Gutenberg corpus: {0:N1} MB across {1} files" -f $totalMb, (Get-ChildItem $gutenbergDir -Filter *.txt).Count) -ForegroundColor Green
Write-Host "`nNext: dotnet run --project tools\WordStrip.NGramBuilder" -ForegroundColor Cyan
