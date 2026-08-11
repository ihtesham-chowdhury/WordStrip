# Project Context

> Handoff document for continuing WordStrip in a fresh Claude Code session.
> Everything below marked **[fact]** was verified by inspecting or running the project.
> Anything marked **[assumption]** or **[recommendation]** is judgement, not established fact.

## 1. Project Identity

- **Project name:** WordStrip **[fact]** (assembly name `WordStrip`, solution `WordStrip.sln`)
- **Project type:** Windows desktop utility — a background tray application with a floating overlay window **[fact]**
- **One-sentence purpose:** Adds phone-keyboard-style word suggestions and offline autocorrect to the physical Windows keyboard, shown in a small floating strip near where you type.
- **Current absolute project path:** `D:\Claude Code\WordStrip` **[fact]**
- **Repository:** Local git repository, created 2026-08-09. No remote. **[fact]**
- **Main branch:** `master` (only branch)
- **Current branch:** `master`
- **Last inspected date:** 2026-08-09

### How the project root was identified

The session's working directory is `D:\Claude Code`, which is the *parent*. The real root is
`D:\Claude Code\WordStrip`, identified by the presence of `WordStrip.sln` and confirmed by
`dotnet sln list` resolving all three projects relative to it. **[fact]**

The `.gitignore` (`bin/`, `obj/`, `publish/`, …) is now live: the initial commit captured 73 files and no
build output. **[fact]**

## 2. Product or Software Description

WordStrip runs in the system tray with no main window. A global low-level keyboard hook watches typing;
while a word is in progress, a small glass strip appears showing ranked candidate words.

**Main workflow:**

1. User types in a supported text field.
2. The strip appears with 3–7 candidates.
3. `Tab` highlights the next candidate (hold to scrub quickly); `Space` inserts the highlighted one;
   `Esc` puts the bar away; clicking a word inserts it directly with no need to press Tab first.
4. On finishing a word (space/punctuation), conservative autocorrect may replace an obvious misspelling.
5. The strip **stays on screen between words**, predicting what comes next (default; switchable). Tab
   cycles those predictions too — see §12 item 4, which records the cost of that.
6. Right-click the tray icon → Settings for theme, word count, persistent bar, bar thickness, glass tint,
   animation speed and bar position, all applying live with a preview.

**Who uses it:** currently the project owner and a small circle of friends/family testing preview builds.
The owner's motivation is that they mistype often and Windows has no equivalent of the Android/iOS
suggestion bar. **[fact — stated by the owner]**

**Privacy posture:** entirely local. No network calls at runtime, no telemetry, no account. The strip
disables itself inside password fields (`ES_PASSWORD`), and so does personal learning. By default nothing
typed is written to disk at all; with learning switched on, what is written is counts of words, pairs and
triples — never text. Everything lives in `%LOCALAPPDATA%\WordStrip` as plain files the user can read and
delete. **[fact]**

## 3. Current Status

- **Overall status:** **All six planned phases are complete, built as 0.10.0.** Only Phase 7 (TSF) remains
  from the original plan. The long-running partial-insertion bug is **fixed and confirmed by the owner**.
  **[fact]**
- **Completed:**
  - Keyboard hook, text injection, word-buffer tracking
  - Offline SymSpell + frequency prediction and autocorrect
  - Floating glass strip with 7 selectable themes, selection lens and position indicator
  - Spring-based motion system with accessibility fallbacks
  - Settings window with live light/dark preview
  - Single-instance handling, tray icon, autostart, installer + portable exe
  - Phase 1 engine hardening: `PrefixIndex`, `ICandidateRanker`/`FrequencyRanker`, additive
    `Suggestion` metadata, performance harness **[fact]**
  - Persistent bar + settings toggle, `IFocusedControlProvider` seam, an end-to-end regression script,
    and the 0.4.0 installer **[fact]**
  - Phase 2: trigram/bigram language model, contextual ranking, typing-history capture, an offline model
    builder, and the 0.5.0 build **[fact]**
  - **Tab fix: the bar claims Tab whenever it is visible** (reported from real use — see §14) **[fact]**
  - **Phase 3: personal vocabulary with casing preservation, autocorrect protection and a settings UI** **[fact]**
  - **Phase 4: personal learning with bounding, decay, cold-start ramp and privacy controls** **[fact]**
  - Phase 3 + Phase 4 + the 0.6.0 build **[fact]**
  - **Phase 5: multi-word phrase suggestions via bounded beam search** **[fact]**
  - **Emoji suggestions, animation-off at the far end of the speed slider, and a single-batch
    `SendInput` fix for partially-inserted replacements** **[fact]**
  - Phase 5 + emoji + the 0.7.0 build **[fact]**
  - **Text insertion by window message rather than synthetic keystrokes — the fix for the partial-insertion
    bug, confirmed by the owner. 233 ms → 5.9 ms for a 51-character entry.** **[fact]**
  - **Apple-style bounce on the bar's entrance and exit** **[fact]**
  - **Phase 6: ONNX neural reranking, optional 227 MB download, with the full cascade, async execution,
    cancellation and stale-result suppression** **[fact]**
  - **271 unit tests and the 0.10.0 build** **[fact]**
- **In progress:** Nothing. Phases 1–6 are closed out.
- **Blocked:** Nothing is blocked. The largest *limitation* (browser/Electron support) is a known
  architectural gap, addressed by Phase 7 (TSF), not a blocker.
- **Next priority:** Owner's call. Phase 7 (TSF) is the only planned phase left and is by far the largest —
  it is what would make WordStrip work in browsers and Electron apps. Do **not** start it unasked.

### Phase 6 — as shipped **[fact]**

**Model:** DistilGPT2, int8 ONNX. **227 MB download** (verified against the publisher, *not* the ~90 MB
quoted during planning, which was an estimate and wrong by nearly three times). Apache 2.0 from the upstream
`distilbert/distilgpt2`; the ONNX conversion at `onnx-community/distilgpt2-ONNX` carries no licence
statement of its own, so upstream terms apply. CPU only.

**Optional download, never bundled.** The installer grew **66.1 → 70.7 MB (+4.6 MB)** for ONNX Runtime —
also better than the ~15 MB warned about. The app is fully functional with no model, which is the default
state.

**Measured:** cold load 3.0–4.4 s (background thread, never blocks startup) · warm inference **54–80 ms** ·
one pass scores the whole candidate list, so five candidates cost the same as one · ~2.2 MB allocated per
call · ~550 MB process working set with the model loaded.

**Honest quality note:** first-token scoring on an 82M model quantised to int8 gives a useful but coarse
signal. It reliably rejects nonsense — "forward" beats "banana" after "i am looking" — and it clearly reads
context, scoring "you" about 7.5 nats higher after "thank" than after "looking". It does **not** reliably
make finer calls: after "i am really looking" it narrowly prefers "you" to "forward". The tests assert what
it can actually do, not what would be nice.

## 4. Directory Structure

```
D:\Claude Code\WordStrip\
├── WordStrip.sln                     Solution: App, Core, Core.Tests
├── README.md                         Engineering documentation (design + rationale)
├── CLAUDE_PROJECT_CONTEXT.md         This file
├── build-release.ps1                 One-shot: publish portable exe + build installer
├── .gitignore                        bin/, obj/, publish/, .corpus/
│
├── .corpus/                          Downloaded corpus (gitignored, ~47 MB). Regenerate, don't commit.
│
├── assets/
│   ├── dict/frequency_dictionary_en_82_765.txt   SymSpell word+frequency list (MIT)
│   ├── ngram/ngram-2.txt, ngram-3.txt            NEW: generated model, committed (7.25 MB)
│   └── wordstrip.ico                 Multi-resolution app icon (generated)
│
├── tools/                            Build-time only; never shipped (build-release publishes only the App)
│   ├── ngram/Fetch-Corpus.ps1        Downloads Gutenberg texts + SymSpell bigrams
│   └── WordStrip.NGramBuilder/       Counts, blends, prunes and writes the model files
│
├── installer/
│   ├── WordStrip.iss                 Inno Setup script (per-user install, no UAC)
│   └── READ-ME-FIRST.txt             Tester-facing instructions, copied into publish/
│
├── publish/                          Build output (gitignored, not source)
│   ├── WordStrip-Setup-<ver>.exe
│   ├── portable/WordStrip.exe
│   └── READ-ME-FIRST.txt
│
├── src/WordStrip.Core/               No UI dependencies
│   ├── Input/                        Win32 hooks, injection, key translation, word buffer
│   │   ├── NativeMethods.cs          All P/Invoke declarations
│   │   ├── LowLevelKeyboardHook.cs   WH_KEYBOARD_LL, Suppress flag
│   │   ├── LowLevelMouseHook.cs      WH_MOUSE_LL, ignores clicks on our own windows
│   │   ├── TypingSession.cs          Reconstructs the in-progress word
│   │   ├── Win32TextInjector.cs      SendInput; minimal-diff + case preservation
│   │   ├── ITextInjector.cs          Seam for a future TSF implementation
│   │   └── KeyTranslator.cs          vkCode → character via layout
│   ├── Personal/                     ← Phases 3 and 4. Local files only; no network code anywhere below.
│   │   ├── PersonalWord.cs           NEW: key + display casing + usage
│   │   ├── PersonalVocabularyStore.cs NEW: the user's own words; atomic writes, corruption-tolerant
│   │   └── PersonalLanguageModel.cs  NEW: learned counts, bounded, decaying, cold-start ramped
│   ├── Prediction/                   ← Phases 1 and 2 both landed here
│   │   ├── FrequencyDictionary.cs    Vocabulary + frequency source
│   │   ├── PrefixIndex.cs            Sorted array + binary search; cached top-frequency list
│   │   ├── SymSpellIndex.cs          Delete-variant fuzzy candidate generation
│   │   ├── DamerauLevenshtein.cs     Bounded edit distance
│   │   ├── ICandidateRanker.cs       Ranking abstraction + RankingContext (+ PredictionContext)
│   │   ├── FrequencyRanker.cs        Deterministic banded scoring — Phase 1, untouched by Phase 2
│   │   ├── ContextualRanker.cs       NEW: wraps the above and adds the n-gram signal
│   │   ├── PredictionContext.cs      NEW: partial word, preceding words, sentence-start
│   │   ├── PredictionEngine.cs       Candidate orchestration; GetLiveSuggestions + GetNextWords
│   │   ├── PhraseGenerator.cs        NEW: bounded beam search for multi-word candidates
│   │   ├── EmojiSuggester.cs         NEW: keyword matching, ambiguity refusal
│   │   ├── EmojiTable.cs             NEW: ~300 curated keyword→emoji entries
│   │   ├── Suggestion.cs             Candidate contract (+ Source, Score, Confidence, WordCount)
│   │   └── NGram/
│   │       ├── NGramFormat.cs        NEW: on-disk contract, shared with the builder
│   │       ├── NGramTokenizer.cs     NEW: shared tokenizer — drift here silently kills every lookup
│   │       └── NGramLanguageModel.cs NEW: tables, stupid backoff, ContextLookup
│   ├── Suggestions/
│   │   ├── SuggestionController.cs   The only class the UI talks to; owns the persistent-bar state machine
│   │   └── SuggestionUpdate.cs       Render contract (+ IsIdle — decides whether the bar may claim keys)
│   ├── Automation/                   Focused-control + caret detection
│   │   ├── FocusedControlInspector.cs    Static live Win32 inspection
│   │   └── IFocusedControlProvider.cs    NEW: seam over the above, so focus can be faked in tests
│   ├── Settings/                     AppSettings, store, enums (BarTheme, BarPosition, BackdropBlur)
│   │   └── UserDataLocation.cs       NEW: one place decides where user data lives; WORDSTRIP_DATA_DIR
│   └── Platform/AutostartManager.cs  HKCU Run key
│
├── src/WordStrip.App/                WPF, net8.0-windows
│   ├── App.xaml.cs                   Composition root — subscription order is load-bearing
│   ├── SingleInstance.cs             Mutex + "show settings" signal
│   ├── Coordination/BarInputRouter.cs   Tab/Space/Enter/Esc handling
│   ├── UI/
│   │   ├── SuggestionBarWindow.xaml(.cs)  The floating strip
│   │   ├── SettingsWindow.xaml(.cs)       Settings + live preview
│   │   ├── Theming/ThemeCatalog.cs        THE SEVEN THEMES live here
│   │   ├── Theming/ThemeTokens.cs         Token contract
│   │   ├── ThemeBrushes.cs                Tokens → frozen brushes
│   │   ├── GlassPlate.cs / SelectionLens.cs   Custom render-only elements
│   │   ├── GlassMetrics.cs / MotionProfile.cs / SpringEase.cs
│   │   ├── SquircleGeometry.cs             Continuous-curvature corners
│   │   ├── BackgroundProbe.cs              Screen-luminance sampling
│   │   ├── SystemAppearance.cs             Accessibility preferences
│   │   └── FrameProbe.cs                   Opt-in frame-time diagnostic
│   ├── Interop/GlassWindowBehavior.cs      No-activate, no-taskbar window
│   └── app.manifest                        PerMonitorV2 DPI awareness
│
├── tests/WordStrip.Core.Tests/       xUnit, 248 tests
│   ├── TestVocabulary.cs             Small hand-written vocabulary + fixture
│   ├── PrefixCompletionTests.cs
│   ├── FuzzyMatchingTests.cs
│   ├── AutocorrectionTests.cs
│   ├── RankingTests.cs
│   ├── PrefixIndexTests.cs
│   ├── SuggestionControllerTests.cs  Persistent-bar state machine
│   ├── TestLanguageModel.cs          NEW: hand-written n-gram fixture + its own vocabulary
│   ├── NGramLanguageModelTests.cs    NEW: backoff, sentence boundaries, determinism, parsing
│   ├── ContextualPredictionTests.cs  The two modes and the ranking contract
│   ├── TypingHistoryTests.cs         When context must be forgotten
│   ├── PersonalVocabularyTests.cs    NEW: store, casing, corruption, autocorrect protection (36)
│   ├── PersonalLearningTests.cs      NEW: counts, bounds, decay, cold start, privacy (28)
│   └── PerformanceTests.cs           Timings against the real vocabulary and the real model
│
└── tests/regression/                 NEW: end-to-end, drives a real Win32 edit control
    ├── Verify-PersistentBar.ps1      6 checks; see §12 for the traps it took to get right
    └── TestTarget.ps1                Throwaway window with a real "Edit" control
```

## 5. Technology Stack

| Item | Version / detail | Source |
|---|---|---|
| Language | C# 12 | **[fact]** |
| Runtime | .NET 8 (`net8.0-windows`) | **[fact]** |
| SDK installed | 8.0.423 | **[fact]** `dotnet --version` |
| UI framework | WPF | **[fact]** |
| Secondary UI | WinForms — **only** for the tray `NotifyIcon` | **[fact]** |
| Tests | xUnit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0 | **[fact]** |
| Installer | Inno Setup 6 (`ISCC.exe` at `%LOCALAPPDATA%\Programs\Inno Setup 6`) | **[fact]** |
| OS (dev machine) | Windows 11, build 10.0.26200 | **[fact]** |
| Display (dev machine) | 1920×1080 at **150% scaling** — matters for capture/testing | **[fact]** |
| Dictionary | SymSpell `frequency_dictionary_en_82_765.txt` (MIT), top 60,000 loaded | **[fact]** |
| Language model | Own format, built from Project Gutenberg (public domain) + SymSpell bigrams (MIT) | **[fact]** |
| External services | **None at runtime.** No APIs, no database, no network calls | **[fact]** |

There are **no NuGet dependencies in the shipping app** — only the test project has package references. **[fact]**

⚠️ **The app makes no network calls, but the *build* does.** `tools\ngram\Fetch-Corpus.ps1` downloads ~47 MB
of corpus text to regenerate the model. That is a developer step, not something the shipped app ever does —
the model is committed under `assets\ngram\` and embedded in the exe. **[fact]**

## 6. Architecture and Data Flow

```
keystroke
   ↓
LowLevelKeyboardHook (WH_KEYBOARD_LL)
   ↓  (subscription order is load-bearing — see §13)
BarInputRouter ── consumes Tab/Space/Enter/Esc, sets e.Suppress
   ↓
TypingSession ── skips suppressed keys; rebuilds the in-progress word
   ↓  CurrentWordChanged / WordCommitted / BufferReset
SuggestionController ── the only class the UI talks to
   ├→ FocusedControlInspector  (is this a text field? a password box? where is the caret?)
   ├→ PredictionEngine → PrefixIndex + SymSpellIndex → FrequencyRanker
   └→ ITextInjector (Win32TextInjector) — deferred via postToMessageLoop
   ↓  SuggestionsChanged(SuggestionUpdate{ Suggestions, Caret })
SuggestionBarWindow ── renders; never takes keyboard focus
```

**Key data-flow rules:**

- Text injection **never** happens inside the hook callback. `SuggestionController` takes a
  `postToMessageLoop` delegate and routes every replacement through it. Injecting from inside the hook
  either gets discarded (when the key is suppressed) or interleaves with the in-flight keystroke and
  corrupts the text. **[fact — this was a real bug, fixed]**
- The prediction layer contains **no UI code** and the UI contains no prediction logic.
- Settings are a single shared mutable `AppSettings` instance; the settings window mutates it and calls
  back into the bar, so changes apply on the next keystroke with no event plumbing.

## 7. Setup and Installation

```powershell
# Prerequisite: .NET 8 SDK (already installed on this machine, 8.0.423)
winget install --id Microsoft.DotNet.SDK.8 -e

# Optional, only needed to build the installer:
winget install --id JRSoftware.InnoSetup -e

# Restore + build
cd "D:\Claude Code\WordStrip"
dotnet build
```

There are **no secrets, API keys, connection strings or `.env` files** in this project, and none are
required. **[fact]**

## 8. Run, Build, Test, and Debug Commands

```powershell
# Build the whole solution
dotnet build "D:\Claude Code\WordStrip\WordStrip.sln" -c Release
```

```powershell
# Run the app (tray only — no main window appears)
& "D:\Claude Code\WordStrip\src\WordStrip.App\bin\Release\net8.0-windows\WordStrip.exe"
```

```powershell
# Open the settings window directly (also what the Start Menu shortcut does)
& "D:\Claude Code\WordStrip\src\WordStrip.App\bin\Release\net8.0-windows\WordStrip.exe" --settings
```

```powershell
# Run all unit tests (248)
dotnet test "D:\Claude Code\WordStrip\tests\WordStrip.Core.Tests\WordStrip.Core.Tests.csproj"
```

```powershell
# Regenerate the n-gram model. Downloads ~47 MB of corpus the first time; takes about a minute to build.
powershell -File "D:\Claude Code\WordStrip\tools\ngram\Fetch-Corpus.ps1"
dotnet run --project "D:\Claude Code\WordStrip\tools\WordStrip.NGramBuilder" -c Release
```

```powershell
# Same, with looser pruning for a bigger model (defaults: 3 / 3 / 12)
dotnet run --project "D:\Claude Code\WordStrip\tools\WordStrip.NGramBuilder" -c Release -- --min-bigram 2 --min-trigram 2 --top 16
```

```powershell
# End-to-end regression: 6 checks against a real Win32 edit control. Takes over the keyboard and the
# foreground for about a minute. Pass -ExePath to test the portable or installed build instead.
powershell -File "D:\Claude Code\WordStrip\tests\regression\Verify-PersistentBar.ps1"
```

```powershell
# Print the performance measurements (needs detailed logger to see the numbers)
dotnet test "D:\Claude Code\WordStrip\tests\WordStrip.Core.Tests\WordStrip.Core.Tests.csproj" --logger "console;verbosity=detailed" --filter "FullyQualifiedName~PerformanceTests"
```

```powershell
# Produce publish\portable\WordStrip.exe and publish\WordStrip-Setup-<ver>.exe
powershell -File "D:\Claude Code\WordStrip\build-release.ps1"
```

```powershell
# Silent install / uninstall of a built installer (useful for verification)
# In-place upgrade. Same AppId, so it replaces the existing install and PRESERVES settings.
# /TASKS=startupicon keeps the autostart entry; /TASKS= (empty) would leave an existing one untouched.
# Do NOT verify by uninstalling and reinstalling — uninstall deletes %LOCALAPPDATA%\WordStrip (§9).
Start-Process "D:\Claude Code\WordStrip\publish\WordStrip-Setup-0.7.0.exe" -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/TASKS=startupicon" -Wait
```

**Debugging aids (opt-in via environment variable, off by default):**

- `WORDSTRIP_FRAMELOG=1` — `FrameProbe` writes per-frame intervals to `%TEMP%\wordstrip_frames.log`,
  used to diagnose animation smoothness. **[fact]**

**IMPORTANT — the app locks its own exe.** Always stop the running process *before* building, or the
build fails with MSB3027 "file is locked":

```powershell
Stop-Process -Name "WordStrip*" -Force -ErrorAction SilentlyContinue
```

## 9. Configuration and Environment Variables

| Name | Kind | Purpose |
|---|---|---|
| `%LOCALAPPDATA%\WordStrip\settings.json` | File | User settings, written by the app |
| `%LOCALAPPDATA%\WordStrip\personal-vocabulary.json` | File | Phase 3: the user's own words, casing and usage |
| `%LOCALAPPDATA%\WordStrip\personal-language-model.json` | File | Phase 4: learned counts. Absent unless learning is on |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WordStrip` | Registry | Autostart entry |
| `WORDSTRIP_FRAMELOG` | Env var (optional) | `1` enables frame-time diagnostics |

- **Ports / URLs / external services:** none. **[fact]**
- **Secrets:** none required. **[fact]**

`settings.json` shape (all optional; missing values fall back to defaults):

```json
{
  "Theme": 0,               // BarTheme enum 0..6
  "SuggestionCount": 4,     // 3..7
  "GlassTint": 0.62,        // 0.15..0.95
  "BarScale": 1.0,          // 0.7..1.4
  "MotionSpeed": 1.0,       // 0.5..2.5
  "BackdropBlur": 3,        // Auto=3 (see §12 — this setting is currently inert)
  "BarPosition": 0,         // 0 bottom, 1 follow caret, 2 top
  "AutocorrectEnabled": true,
  "PersistentBar": true,          // strip stays on screen between words
  "PersonalLearningEnabled": false, // Phase 4 — deliberately off until asked for
  "StartWithWindows": false
}
```

An older `settings.json` with no `PersistentBar` key simply gets the default (`true`) — the upgrade needs
no migration. **[fact — verified on the 0.3.0 → 0.4.0 in-place upgrade]**

⚠️ The installer's `[UninstallDelete]` removes `%LOCALAPPDATA%\WordStrip` entirely, so an
**uninstall/reinstall cycle destroys the user's settings** while an in-place upgrade preserves them. Back
that file up before any install testing. **[fact]**

## 10. Important Files

Inspect these first, roughly in this order:

| File | Why it matters |
|---|---|
| `README.md` | Engineering rationale: why decisions were made, including several hard-won bug post-mortems |
| `src/WordStrip.App/App.xaml.cs` | Composition root. Hook subscription order here is load-bearing |
| `src/WordStrip.Core/Suggestions/SuggestionController.cs` | The seam between input, prediction and UI; owns the persistent-bar state machine |
| `src/WordStrip.App/Coordination/BarInputRouter.cs` | Decides when the bar may claim keys — read alongside `SuggestionUpdate.IsIdle` |
| `tests/regression/Verify-PersistentBar.ps1` | How input behaviour is actually verified; its comments record several dead ends |
| `src/WordStrip.Core/Prediction/PredictionEngine.cs` | Orchestrates both modes: `GetLiveSuggestions` and `GetNextWords` |
| `src/WordStrip.Core/Prediction/NGram/NGramLanguageModel.cs` | The contextual model, backoff, and the `ContextLookup` fast path |
| `src/WordStrip.Core/Prediction/ContextualRanker.cs` | How context is weighed against frequency, and why it's capped |
| `src/WordStrip.Core/Prediction/FrequencyRanker.cs` | Phase 1's banded scoring; Phase 3 should wrap it, not edit it |
| `src/WordStrip.App/UI/Theming/ThemeCatalog.cs` | All seven themes; the only place visual differences live |
| `src/WordStrip.App/UI/SuggestionBarWindow.xaml.cs` | Bar lifecycle, motion, placement, adaptivity |
| `src/WordStrip.Core/Input/TypingSession.cs` | Word-buffer rules and why keys are skipped |
| `tests/WordStrip.Core.Tests/PerformanceTests.cs` | Current performance baseline |

## 11. Recent Work

**Most recent session (Tab fix + Phases 3 and 4, 0.6.0):**

| File | Change | Reason |
|---|---|---|
| `Coordination/BarInputRouter.cs` | Single `_isBarActive`; Esc suppressed only with a selection | Reported: Tab did nothing on the predictions that appear after inserting a word |
| `Personal/PersonalVocabularyStore.cs` | **New.** JSON store, atomic writes, normalized key + display casing | Phase 3; the casing split is what keeps "GitHub" from becoming "github" |
| `Personal/PersonalWord.cs` | **New.** Entry record | Date-only recency, so the file can't become a timeline of someone's day |
| `Personal/PersonalLanguageModel.cs` | **New.** Learned uni/bi/trigram counts, bounded and decaying | Phase 4 |
| `Prediction/PredictionEngine.cs` | Personal completions merged in; `IsCorrectlySpelled` consults them | Personal words must be suggestable and must not be autocorrected away |
| `Prediction/ContextualRanker.cs` | `PersonalBonus` (≤30) and `LearnedBonus` (≤15) | Bounded so neither can cross a band |
| `Settings/AppSettings.cs` | `PersonalLearningEnabled`, default **off** | It changes what is recorded about the user, not how the app looks |
| `Input/WordCommittedEventArgs.cs` | Added `PrecedingWords`, snapshotted pre-update | The history is mutated differently per boundary char; learning needs the exact pair |
| `Suggestions/SuggestionController.cs` | Learning behind the existing focus check; learns the corrected word | Never learn from a field we could not identify, or teach back a typo we just fixed |
| `App.xaml.cs` | Loads both stores; 30 s save timer; final flush on exit | Batched so disk I/O stays off the typing path |
| `UI/SettingsWindow.xaml(.cs)`, `SettingsViewModel.cs` | "Your words" and "Learning" cards | Without a UI the vocabulary is unreachable and learning is unauditable |
| `tests/Personal*Tests.cs` | **New**, 64 tests | Real files in a temp dir — persistence is most of what these classes do |
| `tests/PerformanceTests.cs` | Storage-growth measurement | Phase 4 requires measured growth |

**Session before that (Phase 2 — contextual prediction, 0.5.0):**

| File | Change | Reason |
|---|---|---|
| `Prediction/NGram/NGramLanguageModel.cs` | **New.** Tables, stupid backoff, `ContextLookup` | The contextual signal Phase 2 exists to add |
| `Prediction/NGram/NGramTokenizer.cs` | **New.** Shared by builder and app | Drift between the two silently kills every lookup |
| `Prediction/NGram/NGramFormat.cs` | **New.** On-disk contract | Same reason — a mismatch would not fail to compile |
| `Prediction/PredictionContext.cs` | **New.** Partial word, preceding words, sentence-start | Passed in from above; Phase 7 replaces how it's filled, not what it is |
| `Prediction/ContextualRanker.cs` | **New.** Wraps `FrequencyRanker`, adds a capped bonus | "Add a ranker, don't edit the engine" |
| `Prediction/ICandidateRanker.cs` | `RankingContext` gained `PredictionContext` **with a default** | Additive, so no call site changed |
| `Prediction/PredictionEngine.cs` | `GetNextWords`, contextual `GetLiveSuggestions` overload | The two modes the phase brief separates |
| `Input/TypingSession.cs` | Last-two-words history, `NoteWordInserted`, `ReplaceLastWord` | The model needs context, and it must expire exactly when the buffer does |
| `Suggestions/SuggestionController.cs` | Builds the context; accept and autocorrect keep history honest | Predict from what is on screen, not from what was typed |
| `tools/ngram/`, `tools/WordStrip.NGramBuilder/` | **New** | The model has to be reproducible, not a mystery binary |
| `assets/ngram/*.txt` | **New**, committed, 7.25 MB | Generated output; the corpus itself stays out of the repo |
| `tests/*` | 4 new files, 47 new tests | Backoff, boundaries, ranking contract, history expiry |

**Session before those (persistent bar, 0.4.0):**

| File | Change | Reason |
|---|---|---|
| `Suggestions/SuggestionController.cs` | `PublishIdle`, sticky `Dismiss`, `PollFocus`; `AcceptSuggestion` no longer bails on an empty buffer | The strip vanishing on every committed word read as flicker |
| `Suggestions/SuggestionUpdate.cs` | Added `IsIdle` **with a default** | Separates "the bar is visible" from "the bar owns the keyboard" — see §12 item 12 |
| `Automation/IFocusedControlProvider.cs` | **New.** Seam + `Win32FocusedControlProvider` | The focus check was a static reading live Win32 state, so nothing depending on it was testable |
| `Settings/AppSettings.cs` | `PersistentBar` (default `true`) | Owner asked for a switch back to per-word behaviour |
| `Coordination/BarInputRouter.cs` | Split `_isBarActive` into `_isCompleting` / `_isIdleVisible` | A persistent bar keyed on visibility swallowed Tab and Esc system-wide |
| `App.xaml.cs` | Mouse dismissal wired **before** `Attach()`; 1 s `DispatcherTimer` → `PollFocus` | Ordering stops a one-frame flash; nothing reports Alt+Tab |
| `UI/SettingsViewModel.cs`, `SettingsWindow.xaml` | Persistent-bar checkbox + hint | — |
| `tests/.../SuggestionControllerTests.cs` | **New**, 18 tests | Covers the idle content, dismissal rules and empty-buffer insert |
| `tests/regression/*` | **New** | The unit tests cannot reach hook-driven behaviour; this is how the Tab regression was caught |
| `installer/READ-ME-FIRST.txt`, `README.md` | Rewritten for 0.4.0 | Also dropped a stale blur setting mention and a wrong `BitmapCache` claim |

**Immediately prior session (Phase 1 — prediction hardening):**

| File | Change | Reason |
|---|---|---|
| `Prediction/PrefixIndex.cs` | **New.** Ordinal-sorted array + binary-search prefix range; cached top-32 frequent words | `GetLiveSuggestions` scanned all 60,000 entries per keystroke |
| `Prediction/ICandidateRanker.cs` | **New.** `RankingContext` + ranker interface | Phase 2 must be able to add contextual probability without rewriting the engine |
| `Prediction/FrequencyRanker.cs` | **New.** Banded deterministic scoring | Raw frequency spans 9 orders of magnitude and swamped every other signal |
| `Prediction/Suggestion.cs` | Added `Source` and `Score` **with defaults** | Additive so existing 3-argument construction still compiles |
| `Prediction/PredictionEngine.cs` | Delegates ordering to the ranker; added `GetFrequentWords` | Separates candidate *generation* from *ordering* |
| `tests/WordStrip.Core.Tests/*` | **New project**, 61 tests | Phase 1 requires test coverage of the prediction primitives |

**Earlier sessions:** Phase 1 engine hardening (see §14), and before that the theme system — replaced the
single hard-coded palette with a 7-theme token
system, added the position indicator, and switched the bar to `AllowsTransparency="True"`.

## 12. Known Problems

### Limitations (by design, not bugs)

1. **Works only in classic Win32 `Edit`/`RichEdit` controls.** Notepad and most desktop dialogs work.
   **Chrome, Edge, all browsers, Electron apps (Slack, VS Code, Discord) and Microsoft Office do not.**
   This is the single biggest gap. Fixing it properly means implementing a Windows Text Services Framework
   text service. `ITextInjector` exists as the seam for that. **[fact]**
2. **English only** — one bundled dictionary.
3. **No semantic understanding.** Since Phase 2 there is contextual prediction, but it is three words of
   statistics, not meaning: no phrase prediction, no learning from your writing, no idea what a sentence is
   about. Phases 3–6 address this.
4. **Tab belongs to the bar whenever the bar is visible.** Since 0.6.0, at the owner's request. The cost is
   that Tab will not indent or move between dialog fields while the strip is up — which, with the persistent
   bar on, is most of the time you are in a text field. Esc releases it until the next keystroke. **[fact]**
   *[recommendation: this reversed the opposite decision made in 0.4.0, on one report and before the new
   behaviour had been lived with. Watch it. If it grates, a modifier chord is the fallback and
   `BarInputRouter` is the only file involved.]*
5. **The corpus skews literary.** Mostly 19th- and early-20th-century novels, so ordinary English sentences
   are well covered and modern, technical or workplace phrasing is not. The model has never seen "pull
   request" — though Phase 4's learning will pick that up from the user if they switch it on. **[fact]**
   *[recommendation: a modern conversational corpus would help more than any amount of further tuning.]*
6. **Autocorrect cannot correct *into* a personal word.** Personal words are protected from correction and
   offered as completions, but `SymSpellIndex` is built from the general dictionary at startup and never
   rebuilt, so "githb" will not become "GitHub". **[fact]** *[recommendation: rebuild the fuzzy index when
   the personal vocabulary changes — deliberately out of scope for Phase 3.]*
7. **The word buffer can miss a keystroke if the UI thread is briefly busy.** A low-level keyboard hook is
   only called if the thread that installed it is free to service it; if not, Windows times the hook out and
   the app never sees that key, while the target application still receives it. The buffer is then one
   character short, so a replacement lands with the first typed letter surviving in front of it
   ("aAlexandra Fairbourne Reed"). **[fact — reproduced in the harness roughly one run in six]**
   *[Mitigated rather than fixed: message-based insertion cut the busy window from 80–233 ms to 1–2 ms, and
   it now only reproduces when typing resumes within a few hundred milliseconds of an insertion, which the
   harness did and people do not. A real fix would verify the text before the caret against the buffer
   before replacing, and correct the deletion count when they disagree.]*
8. ⚠️ **Multi-word personal entries reportedly insert only partly — RESOLVED, see §14.** Left here as the
   record of a bug that took three rounds to find. The owner reported
   entries like "Alexandra Fairbourne Reed" arriving truncated, with blank gaps. A real ordering hazard was
   found and fixed (two `SendInput` calls; see §13), but **it did not reproduce in testing** — not against a
   plain `EDIT`, and not against a `RICHEDIT50W` at 25 ms per keystroke. So the fix removes a genuine hazard
   without being confirmed as the cause. **[fact]**
   *[assumption: the likelier culprit is the pre-0.6.0 Tab behaviour. Tab on an idle bar fell through to the
   document as a literal tab character AND hit `IsContextInvalidatingKey`, wiping the word buffer — which
   matches both the truncation and the blank gaps in the report. That was fixed separately. If the owner
   reports it again on 0.7.0, get the exact keystrokes; do not assume this is closed.]*
9. **Phrases inherit the corpus's register.** Multi-word suggestions come from Victorian novels, so "thank
   you" is as likely to suggest "sir said the" as anything from a modern email. Grammatical, just dated.
   Personal learning is the practical mitigation. **[fact]**

### Technical debt / known issues

10. **`BackdropBlur` setting is inert.** Switching to per-pixel alpha (`AllowsTransparency=True`) made real
   DWM Mica/Acrylic impossible, because they cannot apply to a layered window. The UI control was removed
   but the enum and the `AppSettings.BackdropBlur` property remain and are read nowhere meaningful.
   **[fact]** *[recommendation: delete the property and enum, or reintroduce blur via
   `SetWindowCompositionAttribute`, which does work on layered windows but paints a square-cornered
   region.]*
11. **Startup index build takes ~6.2 s** (SymSpell) **plus ~1.5 s** (n-gram model load), measured. Both run
   on a background thread so the tray icon appears immediately, but first-run feels slow. **[fact]**
12. **`FrameProbe` and `BackgroundProbe`** are diagnostic/heuristic helpers; `BackgroundProbe` samples screen
   pixels just outside the bar, which is a heuristic and can mis-read over unusual backgrounds.
13. **Unsigned binaries** — SmartScreen warns on first run for testers. Documented in `READ-ME-FIRST.txt`.
14. **Autostart has two sources of truth that can disagree.** The installer's `startupicon` task writes the
   `HKCU\...\Run` value directly, while the settings window writes both the registry and
   `AppSettings.StartWithWindows`. On the dev machine the Run key is set while `StartWithWindows` is
   `false`, so the settings checkbox shows the wrong state. **[fact — observed 2026-08-09]**
   *[recommendation: have `AppSettings` read the registry as the source of truth rather than caching it.]*

### Testing gotchas discovered the hard way — read before writing UI tests

15. **`PrintWindow` cannot capture a DWM backdrop.** It only captures what the app itself draws. Judging
    translucency from a `PrintWindow` grab is meaningless. **[fact]**
16. **PowerShell is DPI-unaware by default.** Call `SetProcessDPIAware()` first or captures are rendered
    into undersized bitmaps and silently cropped. The dev display is at 150%. **[fact]**
17. **`SetForegroundWindow` is silently refused** from a background process. Use the `AttachThreadInput`
    technique, and always verify the foreground window class before sending keystrokes — otherwise test
    input lands in whatever the user is actually using. **[fact]**
18. **Neither Notepad nor WinForms works as an automated typing target.** Windows 11 ships Notepad as a
    packaged single-instance app: `notepad.exe` exits immediately and `MainWindowHandle` is empty. A
    WinForms `TextBox` reports class `WindowsForms10.EDIT.app.0.<hash>`, which does **not** start with
    `Edit`, so `FocusedControlInspector` ignores it and no bar ever appears. `TestTarget.ps1` creates a
    real `EDIT` control with `CreateWindowEx` instead. **[fact — both tried and discarded]**
19. **`SendKeys` types faster than any keyboard and corrupts the result.** It delivers a whole string in
    microseconds; replacements are deferred onto the message loop, so the burst is still draining into the
    target when the replacement fires and the two interleave — `helo ` came out as `healo`. Send one key at
    a time. This is the harness outrunning hardware, **not** a product defect. **[fact]**
20. **Cold starts need a warm-up word.** The first replacement after launch pays JIT on the whole injection
    path and can land after the check that reads the text back, which looks like a half-applied correction.
    The self-contained single-file build is worse, since it also self-extracts. **[fact]**
21. **Choose test misspellings with exactly one plausible correction.** `helo` is one edit from `help`
    *and* from `hello`, so the tie falls to frequency and `help` wins — correct behaviour, useless
    assertion. `teh` → `the` is unambiguous. **[fact]**
22. **Keep non-ASCII out of string literals in the PowerShell scripts.** They are saved as UTF-8 with no
    byte-order mark, and Windows PowerShell decodes such a file as Windows-1252 — where an em dash's third
    byte becomes a smart closing quote that the parser honours as a string delimiter. It reports as
    "missing terminator" at the *bottom* of the file, hundreds of lines from the dash. Harmless in comments,
    fatal inside a string. **[fact — cost a debugging cycle]**
23. **A bare `EDIT` control has no Ctrl+A.** Select-all comes from the dialog manager, which the test target
    deliberately doesn't run (its pump omits `IsDialogMessage` so Tab reaches the control as a character).
    Clear it with `EM_SETSEL` + `WM_CLEAR`. **[fact]**

## 13. Development Instructions

### Architecture rules

- **`WordStrip.Core` must never reference WPF or contain UI logic.** The prediction layer especially.
- **Theme differences live only in `ThemeCatalog`.** Seven presets over one component, one geometry system,
  one interaction model, one motion system — never seven implementations.
- **Add a ranker; don't edit the engine.** Phase 2 did exactly this: `ContextualRanker` wraps
  `FrequencyRanker.Score` and leaves it untouched. Phase 3's personal vocabulary should arrive the same way.
- **Grow the context types additively.** `RankingContext` gained `PredictionContext` with a default, so
  every existing call site still compiles and still means what it did. Same pattern as `Suggestion.Source`.

### Load-bearing details that look innocuous

- **Hook subscription order — on both hooks.** `BarInputRouter` must subscribe to the keyboard hook
  *before* `TypingSession.Attach()` is called. Handlers run in subscription order and the contract is:
  whatever the router suppresses, `TypingSession` skips. Reverse it and Tab resets the word buffer mid-cycle.
  The mouse hook is the same: the controller's `Dismiss()` must subscribe before `Attach()`, or
  `TypingSession`'s buffer reset republishes the idle list and the bar flashes back on for one frame on
  every outside click.
- **"Visible" and "owns the keyboard" are different conditions.** They were identical until the bar started
  persisting. `SuggestionUpdate.IsIdle` keeps them apart, and `BarInputRouter` routes on
  `_isCompleting`, never on visibility. Collapse them again and a bar that is now up almost continuously
  will swallow Tab and Esc in every text field on the system.
- **`Dismiss()` is sticky on purpose.** It sets a flag that survives until the user types again. Once the
  bar repopulates itself between words, merely hiding the window doesn't stick — the next buffer reset puts
  it straight back. This is why Esc goes through the controller and not `SuggestionBarWindow.HideBar()`.
- **Never call `SendInput` from inside the hook callback.** Always via `postToMessageLoop`.
- **One `SendInput` call per replacement — deletions and text together.** Windows guarantees events inside
  a single call are not interleaved with other input, and guarantees nothing between two calls. Splitting
  them lets the target start on the backspaces while the text is arriving, and they eat its front. This
  shipped once; see §12 item 7.
- **Every ranking bonus stays under the 100-point band gap, and they accumulate.** Context ≤40, personal
  word ≤30, learned usage ≤15. Phrases and emoji sit in the between-words band and are listed explicitly in
  `FrequencyRanker`'s switch — the default is the fuzzy band at zero, which would silently bury them.
- **`INPUT` struct must include the `MOUSEINPUT` union member** even though it is unused — it sets
  `sizeof(INPUT)` to the 40 bytes x64 requires. Without it, `SendInput` silently rejects everything.
- **Injected-key detection uses a private `dwExtraInfo` marker**, not `LLKHF_INJECTED` (which is set for
  *any* process's SendInput, so relying on it would ignore dictation software and automation tools).
- **`GlassPlate` and `SelectionLens` report zero desired size deliberately** so they cannot force the
  window to stay wide. Consequently **do not put a `BitmapCache` on them** — a cache sized from a
  zero-size element renders nothing.
- **A reappearing bar must clear its selection.** Otherwise the next `Space` replaces a word the user
  never chose.
- **The tokenizer is shared between the offline builder and the running app, and must stay that way.** The
  corpus is tokenised once at build time and typed context on every keystroke. If the two ever disagree
  about what a token is — a curly apostrophe, a trailing comma, a capital — every lookup misses. There is
  no error and no crash, just a bar that silently stops predicting.
- **Typing history must be dropped whenever the word buffer is.** Clicks, arrow keys, Ctrl combos,
  backspacing into untracked text, and full stops all clear it. Stale context is worse than none: it
  produces confident, specific predictions from words that are no longer behind the caret.
- **Resolve the n-gram context once per candidate list, not per candidate.** `ContextualRanker` scores up to
  64 completions against the same two preceding words; asking the model per candidate re-derives the
  context and re-probes both tables every time. Measured: 161 µs → 484 µs per keystroke.
- **Don't add raw word frequency on top of a conditional probability.** `P(word | context)` already
  accounts for how common the word is. Double-counting it promotes function words and buries the useful
  predictions — this is why `ProbabilityWeight` is 8 and not 2.
- **Every ranking bonus must stay under the 100-point band gap, and they are cumulative.** Context ≤40,
  personal word ≤30, learned usage ≤15 — 85 in the worst case, plus ~10 of frequency. Add another signal
  without checking the total and a suggestion could outrank a word the user has finished typing.
- **Learning is gated on the same focus check as suggesting.** `SuggestionController.Learn` is the only
  call site, and it sits after `IsSuggestible`. A field the app cannot positively identify must never be
  learned from — "we're not sure" has to mean "don't record it", not "probably fine".
- **`WordCommittedEventArgs.PrecedingWords` is snapshotted before the history updates.** The two branches of
  `CommitIfNonEmpty` leave the history in incompatible states — an ordinary space appends the word, a full
  stop clears it — so reconstructing the context afterwards is wrong in a different way in each case.
- **Both personal stores write via a temp file and `File.Replace`.** The learning model saves on a timer,
  which makes an interrupted write a question of when, not if. A corrupt file loads as empty and is left on
  disk for recovery rather than overwritten.

### UI/UX rules

- The bar must **never take keyboard focus** (`WS_EX_NOACTIVATE`).
- Suggestions and autocorrect are **always disabled in password fields**.
- Respect Windows accessibility: transparency-off / High Contrast → solid surfaces; animations-off →
  instant transitions.
- Motion is spring-based; animations omit `From` so re-triggering continues from the current position.

### Testing expectations

- Prediction primitives are unit-tested; keep them that way.
- UI and input behaviour is verified end-to-end with `tests\regression\Verify-PersistentBar.ps1`, which
  drives a real Win32 `Edit` control and reads text back with `WM_GETTEXT` (**not** screenshots — §12).
  Add a check there for anything a unit test cannot reach; §12 items 17–22 are the traps that cost the most
  time, so read them before extending it. It can drive a plain `EDIT` or a `RICHEDIT50W`
  (`-ControlClass RichEdit`) at any typing rate (`-PerKeyMs`) — a plain `EDIT` processes input too
  synchronously to expose ordering races, which is worth knowing before concluding one is fixed.
- When behaviour in `Core` can't be tested because it reads live Win32 state through a static, add a seam
  for it — `ITextInjector` and `IFocusedControlProvider` are both precedents.
- Performance is measured, not assumed.

## 14. Current Task

**None — Phase 5 is closed out and built as 0.7.0.** The next move belongs to the owner, who is retesting.
Do **not** start Phase 6 without being asked; the phase documents say so explicitly.

The 7-phase plan lives in `C:\Users\wordstrip-dev\Downloads\Worstripe\` (outside the project) as
`PHASE_1..7_*.md`. Phase 5 explicitly **excluded** neural models, LLMs, cloud APIs, TSF and free-form
generative text.

**Delivered in the most recent session (0.7.0) [fact]:**

- **Text injection fix.** Deletions and replacement text now go in one `SendInput` call. Windows only
  guarantees ordering *within* a call, so sending them separately let the still-draining backspaces eat the
  front of the text. **Reported symptom not reproduced** — see §12 item 7 before assuming it is fixed.
- **Phase 5.** `PhraseGenerator` — bounded beam search producing up to 3-word candidates, scored on mean
  log probability per word so length never wins by itself; extensions require trigram evidence; unigram-tier
  seeds are never extended; deduplication keeps one form per opening.
- **Emoji.** `EmojiSuggester` + a curated table of ~300 keywords. At most one, always last, only on an
  unambiguous match, placed by policy rather than scored against words.
- **Animation off.** The far end of the speed slider now disables motion outright instead of setting 80 ms.
- **Harness.** The regression can drive a `RICHEDIT50W` and type at any rate; `WORDSTRIP_DATA_DIR` relocates
  all user data so a run can seed a known vocabulary without touching the real one.
- 57 new unit tests (248 total); regression green on both target control types.

**Measured results, Phase 5 [fact]:**

| Metric | Value |
|---|---|
| Phrase generation | 349 µs/call against the shipped model |
| Unseen context | 13 µs/call (no expansion attempted) |
| Beam | width 6, branching 4, max 3 words |
| Quality floor | mean log probability ≥ −1.4 per word |
| Emoji table | ~300 keywords, at most one suggestion, min prefix 3 |

**Example phrases from the shipped model [fact]:** `i am` → *not going to · very glad to · sure you will* ·
`let me` → *have a good · see the · go to the* · `looking forward` → *to* (single word wins) ·
`how are` → *you* (single word wins).

**Delivered in the session before that (Tab fix + Phases 3 and 4) [fact]:**

- **Tab fix.** Reported from use: after inserting a word the predictions appear immediately but Tab did
  nothing, because the router only claimed keys while a word was in progress. The bar now owns Tab whenever
  it is showing anything. Two guards keep typing intact — Space/Enter only claimed with a selection, Esc
  only swallowed with a selection to cancel.
- **Phase 3.** `PersonalVocabularyStore` (JSON, atomic writes, corruption-tolerant), casing preserved
  separately from the lookup key, personal words folded into completion and protected from autocorrect, a
  bounded personal ranking bonus, and a settings card with add/remove/import/export.
- **Phase 4.** `PersonalLanguageModel` — personal uni/bi/trigram counts learned only from committed words in
  suggestible controls, with count saturation, periodic decay, table pruning, a cold-start confidence ramp,
  and settings for on/off plus clear-everything.
- 65 new unit tests (191 total), storage growth measured, and the regression updated to test next-word
  insertion end to end now that Tab reaches the idle bar.

**Measured results, Phases 3 and 4 [fact]:**

| Metric | Value |
|---|---|
| Personal model growth | 45 KB @ 1k words · 164 KB @ 5k · 193 KB @ 20k · **394 KB @ 100k** |
| Bounds | 20,000 entries per order, counts capped at 1,000 |
| Decay | ×0.9 every 20,000 learned words |
| Cold start | linear ramp to full weight at 2,000 learned words |
| Personal vocabulary cap | 5,000 words, least-used evicted |
| Ranking bonuses | personal word ≤30, learned usage ≤15, context ≤40 — all under the 100-point band gap |
| Installer | 66.1 MB, unchanged from 0.5.0 |

**Delivered in the session before that (Phase 2) [fact]:**

- `NGramLanguageModel` — trigram/bigram tables with stupid-backoff scoring through to a unigram tier
- `PredictionContext` and `NGramTokenizer`, shared by the offline builder and the running app
- `ContextualRanker` wrapping `FrequencyRanker`; `RankingContext` extended additively
- `PredictionEngine.GetNextWords`, and a context-aware overload of `GetLiveSuggestions`
- Typing history in `TypingSession` (last two words), with autocorrect and accept both keeping it honest
- `tools\ngram\Fetch-Corpus.ps1` and `tools\WordStrip.NGramBuilder` — the model is reproducible
- 47 new unit tests (126 total, all passing); end-to-end regression unchanged and passing
- 0.5.0 built (installer +2.0 MB over 0.4.0)

**Measured results, Phase 2 [fact]:**

| Metric | Value |
|---|---|
| Model on disk | 7.25 MB (2.42 bigram + 4.83 trigram) |
| Installer delta | **+2.0 MB** (64.1 → 66.1 MB; the single-file build compresses it ~3:1) |
| Entries | 120,082 bigrams / 227,213 trigrams over 23,352 + 87,319 contexts |
| Corpus | 7,935,098 tokens from 61 Gutenberg books, plus 242,342 SymSpell bigrams |
| Model load | 1476 ms (background thread, startup only) |
| Resident memory | 26.3 MB |
| Next-word lookup | 1.6–2.1 µs |
| Next word, end-to-end | 30.9 µs/call |
| Completion **with** context | 158.2 µs/call |
| Completion without context | 147.3 µs/call (so context costs ~11 µs) |

**Phase 1 baseline, unchanged [fact]:** prefix lookups 4.6–14.7 µs (600–700× faster than the original
scan), autocorrection 274.5 µs, dictionary load 185 ms, SymSpell index build 6187 ms.

**Sample predictions from the shipped model [fact]:**

| Context | Predictions |
|---|---|
| `how are` | you, we, they, all |
| `let me` | see, go, have, know |
| `i am` | not, sure, a, very |
| `thank you` | for, sir, said, i |

**Design decisions taken during implementation, worth knowing about:**

1. **The bar claims Tab whenever it is visible.** *Reversed in 0.6.0.* It originally claimed nothing between
   words, to keep Tab indenting and moving between dialog fields. Phase 2 changed the calculus — the idle
   bar now holds real predictions, and mouse-only put them out of reach on the path where they matter most.
   Esc is the release valve, and Esc itself is only swallowed when there is a selection to cancel.
2. **Personal learning is off by default, and everything else about it is opt-in-shaped.** It is the only
   setting that changes what the app records about its user rather than how it looks or behaves. Clearing
   deletes the file rather than blanking it; the export carries words but no frequencies or dates; learning
   is gated on the same focus check as suggesting, so an unidentifiable field is never learned from.
3. **Personal signals are bounded and cannot cross bands.** A personal word gets at most +30 and learned
   usage at most +15, against a 100-point gap between ranking bands. A word the user has finished typing
   always wins, however much personal evidence argues otherwise.
4. **Accepting with an empty buffer inserts rather than replaces.** Between words there is nothing to
   replace, so the chosen word is simply typed with a trailing space. The guard moved from buffer length to
   focus, so an accept can never inject into a surface we would not have suggested for.
5. **The model stores probabilities, not counts.** Two sources whose raw counts differ by six orders of
   magnitude cannot be summed — Google Books would erase Gutenberg. Each is reduced to a conditional
   distribution and mixed. Where only one source knows a context it is the whole distribution, not half.
6. **Context is weighted above frequency, deliberately.** A conditional probability already accounts for
   how common a word is; adding `log10(frequency)` on top double-counts it. At the original weight, "i am"
   predicted *the* and *to* — real continuations, useless suggestions — and buried *sure*.
7. **The sentence-start marker is a context but never a suggestion.** It was briefly the most probable
   continuation of "thank you", which would have rendered a blank chip.

## 15. Recommended Next Steps

1. **Confirm the insertion bug is actually gone** (§12 item 7). This is the only genuinely open question.
   The fix was not reproducible in testing, so the owner retesting their real phrases is the evidence. If it
   recurs, get the exact keystrokes and the exact output before changing anything else. **[recommendation]**
2. **Watch the Tab decision.** Reversed on one report, before the new behaviour had been lived with; the
   cost — Tab not indenting or moving between fields while the bar is up — lands on every text field.
   `BarInputRouter` is the only file that would change if a modifier chord turns out to be better.
3. **Judge the phrases and emoji by use.** Phrases inherit a Victorian register (§12 item 8) and emoji take
   a slot from a word. Both are switchable, and whether the defaults are right is a question about taste
   that no amount of testing here can answer.
4. **Fix the autostart split-brain** (§12 item 13) — the settings checkbox can disagree with the registry.
5. **Resolve the inert `BackdropBlur` setting** — remove it or reimplement blur.
6. **Consider letting autocorrect correct *into* personal words** (§12 item 6). Rebuilding `SymSpellIndex`
   when the vocabulary changes is the obvious approach and was out of scope.
7. **Only then** consider Phase 6 (neural reranking). Do **not** start it without being asked. The
   integration point is ready: `ICandidateRanker` takes the whole `PredictionContext`, and `Suggestion` now
   carries `Confidence`, so a reranker slots in beside `ContextualRanker` rather than replacing anything.

**Release checklist, for whenever the next build ships:**

- Stop the running app first (it locks its own exe).
- Bump the version in **both** `installer/WordStrip.iss` and `src/WordStrip.App/WordStrip.App.csproj`.
- `dotnet test` (248), then `Verify-PersistentBar.ps1`, then `build-release.ps1`.
- Re-run `Verify-PersistentBar.ps1 -ExePath ...\publish\portable\WordStrip.exe` — the self-contained
  single-file build is a different code path (embedded dictionary) and starts more slowly.
- Back up **the whole of** `%LOCALAPPDATA%\WordStrip` before touching the installer (§9). It now holds the
  user's personal vocabulary and learned data as well as settings, and uninstall deletes the lot.

## 16. Fresh-Chat Startup Prompt

```text
Read D:\Claude Code\WordStrip\CLAUDE_PROJECT_CONTEXT.md first, before doing anything else.

Then:
1. Confirm your actual current working directory, and note that the project root is
   D:\Claude Code\WordStrip (the working directory may be its parent).
2. Inspect the files listed in section 10 of that document before changing anything.
3. Summarise your understanding of the project back to me in a few sentences.
4. State what you plan to do next, based on section 14 (Current Task) and section 15
   (Recommended Next Steps).
5. Ask questions only if something is genuinely ambiguous or unsafe. Do not ask about
   anything already answered in the context file.
6. Note that section 14 currently says there is no active task: Phases 3 and 4 are
   built as 0.6.0 but not installed, and 0.5.0 was never trialled either. Ask what they
   want next rather than assuming, and do NOT start Phase 5 unprompted. The thing worth
   raising is that three versions of work are now unvalidated by use — installing and
   living with 0.6.0 is more valuable than any further feature.
7. Respect the "load-bearing details" in section 13 — several of them look like harmless
   code but will silently break text insertion, the layout, or Tab and Esc system-wide
   if changed.
8. Whenever a major decision, feature or task status changes, update
   CLAUDE_PROJECT_CONTEXT.md so it stays accurate.

The project is now under git (local only, branch `master`), so there is an undo. Commit
before substantial changes.
```
