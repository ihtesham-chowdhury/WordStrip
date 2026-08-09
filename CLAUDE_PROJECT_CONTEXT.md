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
5. The strip **stays on screen between words** showing common words (default; switchable). While idle it
   claims no keys — see §12 item 4 and §13, this distinction is load-bearing.
6. Right-click the tray icon → Settings for theme, word count, persistent bar, bar thickness, glass tint,
   animation speed and bar position, all applying live with a preview.

**Who uses it:** currently the project owner and a small circle of friends/family testing preview builds.
The owner's motivation is that they mistype often and Windows has no equivalent of the Android/iOS
suggestion bar. **[fact — stated by the owner]**

**Privacy posture:** entirely local. No network calls, no telemetry, no account. The strip disables itself
inside password fields (`ES_PASSWORD`). The typing buffer holds only the in-progress word, in memory. **[fact]**

## 3. Current Status

- **Overall status:** **Phase 2 is complete and built as 0.5.0.** The installer exists at
  `publish\WordStrip-Setup-0.5.0.exe` but has **not** been installed — 0.4.0 is what is installed and
  running on the dev machine. **[fact]**
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
  - **Phase 2: trigram/bigram language model, contextual ranking, typing-history capture, an offline model
    builder, 126 unit tests, and the 0.5.0 build** **[fact]**
- **In progress:** Nothing. Phase 2 is closed out.
- **Blocked:** Nothing is blocked. The largest *limitation* (browser/Electron support) is a known
  architectural gap, addressed by Phase 7 (TSF), not a blocker.
- **Next priority:** Owner decision. 0.5.0 is built but not installed, and the open interaction question
  from §15 is now much more pressing — see §12 item 4. Do **not** start Phase 3 unasked.

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
│   │   ├── Suggestion.cs             Candidate contract (+ Source, Score)
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
├── tests/WordStrip.Core.Tests/       xUnit, 126 tests
│   ├── TestVocabulary.cs             Small hand-written vocabulary + fixture
│   ├── PrefixCompletionTests.cs
│   ├── FuzzyMatchingTests.cs
│   ├── AutocorrectionTests.cs
│   ├── RankingTests.cs
│   ├── PrefixIndexTests.cs
│   ├── SuggestionControllerTests.cs  Persistent-bar state machine
│   ├── TestLanguageModel.cs          NEW: hand-written n-gram fixture + its own vocabulary
│   ├── NGramLanguageModelTests.cs    NEW: backoff, sentence boundaries, determinism, parsing
│   ├── ContextualPredictionTests.cs  NEW: the two modes and the ranking contract
│   ├── TypingHistoryTests.cs         NEW: when context must be forgotten
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
# Run all unit tests (126)
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
Start-Process "D:\Claude Code\WordStrip\publish\WordStrip-Setup-0.4.0.exe" -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/TASKS=startupicon" -Wait
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
  "PersistentBar": true,    // strip stays on screen between words
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

**Most recent session (Phase 2 — contextual prediction, 0.5.0):**

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

**Session before that (persistent bar, 0.4.0):**

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
4. **The idle bar is mouse-only — and Phase 2 made this matter much more.** Between words you must click a
   predicted word; you cannot Tab to it, because a persistent bar that captured Tab would stop Tab indenting
   and moving between fields system-wide (§13). That was an easy trade when the bar showed generic common
   words. Now that it makes genuine predictions, **the headline feature of Phase 2 is reachable only by
   mouse**, which for a typing aid is a real gap. **[fact]**
   *[recommendation: this is the highest-value open question. A dedicated chord that doesn't collide with
   Tab is the obvious fix, but which one is the owner's call — ask before implementing. Flagged for tester
   feedback in READ-ME-FIRST.txt.]*
5. **The corpus skews literary.** Mostly 19th- and early-20th-century novels, so ordinary English sentences
   are well covered and modern, technical or workplace phrasing is not. The model has never seen "pull
   request". **[fact]** *[recommendation: a modern conversational corpus would help more than any amount of
   further tuning.]*

### Technical debt / known issues

6. **`BackdropBlur` setting is inert.** Switching to per-pixel alpha (`AllowsTransparency=True`) made real
   DWM Mica/Acrylic impossible, because they cannot apply to a layered window. The UI control was removed
   but the enum and the `AppSettings.BackdropBlur` property remain and are read nowhere meaningful.
   **[fact]** *[recommendation: delete the property and enum, or reintroduce blur via
   `SetWindowCompositionAttribute`, which does work on layered windows but paints a square-cornered
   region.]*
7. **Startup index build takes ~6.2 s** (SymSpell) **plus ~1.5 s** (n-gram model load), measured. Both run
   on a background thread so the tray icon appears immediately, but first-run feels slow. **[fact]**
8. **`FrameProbe` and `BackgroundProbe`** are diagnostic/heuristic helpers; `BackgroundProbe` samples screen
   pixels just outside the bar, which is a heuristic and can mis-read over unusual backgrounds.
9. **Unsigned binaries** — SmartScreen warns on first run for testers. Documented in `READ-ME-FIRST.txt`.
10. **Autostart has two sources of truth that can disagree.** The installer's `startupicon` task writes the
   `HKCU\...\Run` value directly, while the settings window writes both the registry and
   `AppSettings.StartWithWindows`. On the dev machine the Run key is set while `StartWithWindows` is
   `false`, so the settings checkbox shows the wrong state. **[fact — observed 2026-08-09]**
   *[recommendation: have `AppSettings` read the registry as the source of truth rather than caching it.]*

### Testing gotchas discovered the hard way — read before writing UI tests

11. **`PrintWindow` cannot capture a DWM backdrop.** It only captures what the app itself draws. Judging
    translucency from a `PrintWindow` grab is meaningless. **[fact]**
12. **PowerShell is DPI-unaware by default.** Call `SetProcessDPIAware()` first or captures are rendered
    into undersized bitmaps and silently cropped. The dev display is at 150%. **[fact]**
13. **`SetForegroundWindow` is silently refused** from a background process. Use the `AttachThreadInput`
    technique, and always verify the foreground window class before sending keystrokes — otherwise test
    input lands in whatever the user is actually using. **[fact]**
14. **Neither Notepad nor WinForms works as an automated typing target.** Windows 11 ships Notepad as a
    packaged single-instance app: `notepad.exe` exits immediately and `MainWindowHandle` is empty. A
    WinForms `TextBox` reports class `WindowsForms10.EDIT.app.0.<hash>`, which does **not** start with
    `Edit`, so `FocusedControlInspector` ignores it and no bar ever appears. `TestTarget.ps1` creates a
    real `EDIT` control with `CreateWindowEx` instead. **[fact — both tried and discarded]**
15. **`SendKeys` types faster than any keyboard and corrupts the result.** It delivers a whole string in
    microseconds; replacements are deferred onto the message loop, so the burst is still draining into the
    target when the replacement fires and the two interleave — `helo ` came out as `healo`. Send one key at
    a time. This is the harness outrunning hardware, **not** a product defect. **[fact]**
16. **Cold starts need a warm-up word.** The first replacement after launch pays JIT on the whole injection
    path and can land after the check that reads the text back, which looks like a half-applied correction.
    The self-contained single-file build is worse, since it also self-extracts. **[fact]**
17. **Choose test misspellings with exactly one plausible correction.** `helo` is one edit from `help`
    *and* from `hello`, so the tie falls to frequency and `help` wins — correct behaviour, useless
    assertion. `teh` → `the` is unambiguous. **[fact]**
18. **A bare `EDIT` control has no Ctrl+A.** Select-all comes from the dialog manager, which the test target
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
  Add a check there for anything a unit test cannot reach; §12 items 14–18 are the traps that cost the most
  time, so read them before extending it.
- When behaviour in `Core` can't be tested because it reads live Win32 state through a static, add a seam
  for it — `ITextInjector` and `IFocusedControlProvider` are both precedents.
- Performance is measured, not assumed.

## 14. Current Task

**None — Phase 2 is closed out and built as 0.5.0.** The next move belongs to the owner. Do **not** start
Phase 3 without being asked; the phase documents say so explicitly.

The 7-phase plan lives in `C:\Users\wordstrip-dev\Downloads\Worstripe\` (outside the project) as
`PHASE_1..7_*.md`. Phase 2 explicitly **excluded** personal vocabulary, personal learning, neural models
and TSF.

**Delivered in the most recent session (Phase 2) [fact]:**

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

1. **The idle bar claims no keys.** Keeping the bar visible while letting the router carry on as before
   makes Tab and Esc unusable system-wide, because "is the bar visible" is true almost continuously once
   the bar persists. Keyboard cycling is scoped to completions; the idle bar is click-only. **Phase 2 made
   this a real cost** — see §12 item 4.
2. **Accepting with an empty buffer inserts rather than replaces.** Between words there is nothing to
   replace, so the chosen word is simply typed with a trailing space. The guard moved from buffer length to
   focus, so an accept can never inject into a surface we would not have suggested for.
3. **The model stores probabilities, not counts.** Two sources whose raw counts differ by six orders of
   magnitude cannot be summed — Google Books would erase Gutenberg. Each is reduced to a conditional
   distribution and mixed. Where only one source knows a context it is the whole distribution, not half.
4. **Context is weighted above frequency, deliberately.** A conditional probability already accounts for
   how common a word is; adding `log10(frequency)` on top double-counts it. At the original weight, "i am"
   predicted *the* and *to* — real continuations, useless suggestions — and buried *sure*.
5. **The sentence-start marker is a context but never a suggestion.** It was briefly the most probable
   continuation of "thank you", which would have rendered a blank chip.

## 15. Recommended Next Steps

**Wait for the owner's feedback on 0.4.0 before doing any of this.** They are using it for a few days, and
the persistent bar is the thing under evaluation.

1. **Decide how a predicted word gets taken from the keyboard** (§12 item 4). This is the biggest open
   question and the one Phase 2 created: the between-words bar now makes real predictions, and they can
   only be clicked. A chord that doesn't collide with Tab is the obvious fix, but *which* is the owner's
   call. **[recommendation — ask, don't guess]**
2. **Install and trial 0.5.0.** It is built but not installed; 0.4.0 is what is running. The corpus skew
   (§12 item 5) is best judged by using it on real writing.
3. **Fix the autostart split-brain** (§12 item 10) — the settings checkbox can disagree with the registry.
4. **Resolve the inert `BackdropBlur` setting** — remove it or reimplement blur.
5. **Only then** consider Phase 3 (personal vocabulary). Do **not** start it without being asked; the phase
   documents say so explicitly. The integration point is ready: `ICandidateRanker` takes a
   `RankingContext` that already carries the full `PredictionContext`, so a personal-vocabulary signal
   arrives as another ranker, and `PredictionContext` itself was built to grow.

**Release checklist, for whenever the next build ships:**

- Stop the running app first (it locks its own exe).
- Bump the version in **both** `installer/WordStrip.iss` and `src/WordStrip.App/WordStrip.App.csproj`.
- `dotnet test` (79), then `Verify-PersistentBar.ps1`, then `build-release.ps1`.
- Re-run `Verify-PersistentBar.ps1 -ExePath ...\publish\portable\WordStrip.exe` — the self-contained
  single-file build is a different code path (embedded dictionary) and starts more slowly.
- Back up `%LOCALAPPDATA%\WordStrip\settings.json` before touching the installer (§9).

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
6. Note that section 14 currently says there is no active task: Phase 2 is built as
   0.5.0 but not installed. Ask what they want next rather than assuming, and do NOT
   start Phase 3 unprompted. The open question worth raising is section 12 item 4 —
   predicted words can only be clicked, not reached from the keyboard.
7. Respect the "load-bearing details" in section 13 — several of them look like harmless
   code but will silently break text insertion, the layout, or Tab and Esc system-wide
   if changed.
8. Whenever a major decision, feature or task status changes, update
   CLAUDE_PROJECT_CONTEXT.md so it stays accurate.

The project is now under git (local only, branch `master`), so there is an undo. Commit
before substantial changes.
```
