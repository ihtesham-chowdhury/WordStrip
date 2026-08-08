# Project Context

> Handoff document for continuing WordStrip in a fresh Claude Code session.
> Everything below marked **[fact]** was verified by inspecting or running the project.
> Anything marked **[assumption]** or **[recommendation]** is judgement, not established fact.

## 1. Project Identity

- **Project name:** WordStrip **[fact]** (assembly name `WordStrip`, solution `WordStrip.sln`)
- **Project type:** Windows desktop utility — a background tray application with a floating overlay window **[fact]**
- **One-sentence purpose:** Adds phone-keyboard-style word suggestions and offline autocorrect to the physical Windows keyboard, shown in a small floating strip near where you type.
- **Current absolute project path:** `D:\Claude Code\WordStrip` **[fact]**
- **Repository:** None. `git rev-parse` returns *"fatal: not a git repository"* — the project is **not under version control** **[fact]**
- **Main branch:** N/A (no repository)
- **Current branch:** N/A (no repository)
- **Last inspected date:** 2026-08-09

### How the project root was identified

The session's working directory is `D:\Claude Code`, which is the *parent*. The real root is
`D:\Claude Code\WordStrip`, identified by the presence of `WordStrip.sln` and confirmed by
`dotnet sln list` resolving all three projects relative to it. **[fact]**

A `.gitignore` exists (`bin/`, `obj/`, `publish/`, …) but no `.git` directory, so the ignore file is
currently inert. **[fact]**

## 2. Product or Software Description

WordStrip runs in the system tray with no main window. A global low-level keyboard hook watches typing;
while a word is in progress, a small glass strip appears showing ranked candidate words.

**Main workflow:**

1. User types in a supported text field.
2. The strip appears with 3–7 candidates.
3. `Tab` highlights the next candidate (hold to scrub quickly); `Space` inserts the highlighted one;
   `Esc` dismisses; clicking a word inserts it directly with no need to press Tab first.
4. On finishing a word (space/punctuation), conservative autocorrect may replace an obvious misspelling.
5. Right-click the tray icon → Settings for theme, word count, bar thickness, glass tint, animation speed
   and bar position, all applying live with a preview.

**Who uses it:** currently the project owner and a small circle of friends/family testing preview builds.
The owner's motivation is that they mistype often and Windows has no equivalent of the Android/iOS
suggestion bar. **[fact — stated by the owner]**

**Privacy posture:** entirely local. No network calls, no telemetry, no account. The strip disables itself
inside password fields (`ES_PASSWORD`). The typing buffer holds only the in-progress word, in memory. **[fact]**

## 3. Current Status

- **Overall status:** Working preview, version 0.3.0 shipped as an installer. **Phase 1 of a 7-phase
  intelligence roadmap is partially complete and uncommitted to an installer.** **[fact]**
- **Completed:**
  - Keyboard hook, text injection, word-buffer tracking
  - Offline SymSpell + frequency prediction and autocorrect
  - Floating glass strip with 7 selectable themes, selection lens and position indicator
  - Spring-based motion system with accessibility fallbacks
  - Settings window with live light/dark preview
  - Single-instance handling, tray icon, autostart, installer + portable exe
  - **Phase 1 engine hardening: `PrefixIndex`, `ICandidateRanker`/`FrequencyRanker`, additive
    `Suggestion` metadata, 61 unit tests, performance harness** **[fact]**
- **In progress:** Phase 1 finishing touches (see §14).
- **Blocked:** Nothing is blocked. The largest *limitation* (browser/Electron support) is a known
  architectural gap, not a blocker for Phase 1.
- **Next priority:** Persistent-bar behaviour + setting, then build and ship the Phase 1 installer (§15).

## 4. Directory Structure

```
D:\Claude Code\WordStrip\
├── WordStrip.sln                     Solution: App, Core, Core.Tests
├── README.md                         Engineering documentation (design + rationale)
├── CLAUDE_PROJECT_CONTEXT.md         This file
├── build-release.ps1                 One-shot: publish portable exe + build installer
├── .gitignore                        Present, but no git repo exists
│
├── assets/
│   ├── dict/frequency_dictionary_en_82_765.txt   SymSpell word+frequency list (MIT)
│   └── wordstrip.ico                 Multi-resolution app icon (generated)
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
│   ├── Prediction/                   ← Phase 1 work happened here
│   │   ├── FrequencyDictionary.cs    Vocabulary + frequency source
│   │   ├── PrefixIndex.cs            NEW: sorted array + binary search; cached top-frequency list
│   │   ├── SymSpellIndex.cs          Delete-variant fuzzy candidate generation
│   │   ├── DamerauLevenshtein.cs     Bounded edit distance
│   │   ├── ICandidateRanker.cs       NEW: ranking abstraction + RankingContext
│   │   ├── FrequencyRanker.cs        NEW: deterministic banded scoring
│   │   ├── PredictionEngine.cs       Candidate orchestration only
│   │   └── Suggestion.cs             Candidate contract (+ Source, Score)
│   ├── Suggestions/SuggestionController.cs   The only class the UI talks to
│   ├── Automation/                   Focused-control + caret detection
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
└── tests/WordStrip.Core.Tests/       xUnit, 61 tests
    ├── TestVocabulary.cs             Small hand-written vocabulary + fixture
    ├── PrefixCompletionTests.cs
    ├── FuzzyMatchingTests.cs
    ├── AutocorrectionTests.cs
    ├── RankingTests.cs
    ├── PrefixIndexTests.cs
    └── PerformanceTests.cs           Timings against the real 60k vocabulary
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
| External services | **None.** No APIs, no database, no network calls | **[fact]** |

There are **no NuGet dependencies in the shipping app** — only the test project has package references. **[fact]**

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
# Run all unit tests
dotnet test "D:\Claude Code\WordStrip\tests\WordStrip.Core.Tests\WordStrip.Core.Tests.csproj"
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
Start-Process "D:\Claude Code\WordStrip\publish\WordStrip-Setup-0.3.0.exe" -ArgumentList "/VERYSILENT","/SUPPRESSMSGBOXES","/NORESTART","/TASKS=" -Wait
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
  "StartWithWindows": false
}
```

## 10. Important Files

Inspect these first, roughly in this order:

| File | Why it matters |
|---|---|
| `README.md` | Engineering rationale: why decisions were made, including several hard-won bug post-mortems |
| `src/WordStrip.App/App.xaml.cs` | Composition root. Hook subscription order here is load-bearing |
| `src/WordStrip.Core/Suggestions/SuggestionController.cs` | The seam between input, prediction and UI |
| `src/WordStrip.Core/Prediction/PredictionEngine.cs` | Where Phase 2 plugs in (`GetFrequentWords` is the context seam) |
| `src/WordStrip.Core/Prediction/FrequencyRanker.cs` | Ranking rules; Phase 2 adds a ranker rather than editing this |
| `src/WordStrip.App/UI/Theming/ThemeCatalog.cs` | All seven themes; the only place visual differences live |
| `src/WordStrip.App/UI/SuggestionBarWindow.xaml.cs` | Bar lifecycle, motion, placement, adaptivity |
| `src/WordStrip.Core/Input/TypingSession.cs` | Word-buffer rules and why keys are skipped |
| `tests/WordStrip.Core.Tests/PerformanceTests.cs` | Current performance baseline |

## 11. Recent Work

**Most recent session (Phase 1 — prediction hardening):**

| File | Change | Reason |
|---|---|---|
| `Prediction/PrefixIndex.cs` | **New.** Ordinal-sorted array + binary-search prefix range; cached top-32 frequent words | `GetLiveSuggestions` scanned all 60,000 entries per keystroke |
| `Prediction/ICandidateRanker.cs` | **New.** `RankingContext` + ranker interface | Phase 2 must be able to add contextual probability without rewriting the engine |
| `Prediction/FrequencyRanker.cs` | **New.** Banded deterministic scoring | Raw frequency spans 9 orders of magnitude and swamped every other signal |
| `Prediction/Suggestion.cs` | Added `Source` and `Score` **with defaults** | Additive so existing 3-argument construction still compiles |
| `Prediction/PredictionEngine.cs` | Delegates ordering to the ranker; added `GetFrequentWords` | Separates candidate *generation* from *ordering* |
| `tests/WordStrip.Core.Tests/*` | **New project**, 61 tests | Phase 1 requires test coverage of the prediction primitives |

**Immediately prior session (theme system):** replaced the single hard-coded palette with a 7-theme token
system, added the position indicator, and switched the bar to `AllowsTransparency="True"`.

## 12. Known Problems

### Limitations (by design, not bugs)

1. **Works only in classic Win32 `Edit`/`RichEdit` controls.** Notepad and most desktop dialogs work.
   **Chrome, Edge, all browsers, Electron apps (Slack, VS Code, Discord) and Microsoft Office do not.**
   This is the single biggest gap. Fixing it properly means implementing a Windows Text Services Framework
   text service. `ITextInjector` exists as the seam for that. **[fact]**
2. **English only** — one bundled dictionary.
3. **No context awareness.** Suggestions are prefix + edit distance + corpus frequency. Phase 2 addresses this.

### Technical debt / known issues

4. **`BackdropBlur` setting is inert.** Switching to per-pixel alpha (`AllowsTransparency=True`) made real
   DWM Mica/Acrylic impossible, because they cannot apply to a layered window. The UI control was removed
   but the enum and the `AppSettings.BackdropBlur` property remain and are read nowhere meaningful.
   **[fact]** *[recommendation: delete the property and enum, or reintroduce blur via
   `SetWindowCompositionAttribute`, which does work on layered windows but paints a square-cornered
   region.]*
5. **SymSpell index build takes ~6.2 s at startup** (measured). It runs on a background thread so the tray
   icon appears immediately, but first-run feels slow. **[fact]**
6. **No git repository.** There is no version history and no way to diff or revert. **[fact]**
   *[recommendation: `git init` early in the next session.]*
7. **`FrameProbe` and `BackgroundProbe`** are diagnostic/heuristic helpers; `BackgroundProbe` samples screen
   pixels just outside the bar, which is a heuristic and can mis-read over unusual backgrounds.
8. **Unsigned binaries** — SmartScreen warns on first run for testers. Documented in `READ-ME-FIRST.txt`.

### Testing gotchas discovered the hard way — read before writing UI tests

9. **`PrintWindow` cannot capture a DWM backdrop.** It only captures what the app itself draws. Judging
   translucency from a `PrintWindow` grab is meaningless. **[fact]**
10. **PowerShell is DPI-unaware by default.** Call `SetProcessDPIAware()` first or captures are rendered
    into undersized bitmaps and silently cropped. The dev display is at 150%. **[fact]**
11. **`SetForegroundWindow` is silently refused** from a background process. Use the `AttachThreadInput`
    technique, and always verify the foreground window class before sending keystrokes — otherwise test
    input lands in whatever the user is actually using. **[fact]**

## 13. Development Instructions

### Architecture rules

- **`WordStrip.Core` must never reference WPF or contain UI logic.** The prediction layer especially.
- **Theme differences live only in `ThemeCatalog`.** Seven presets over one component, one geometry system,
  one interaction model, one motion system — never seven implementations.
- **Add a ranker; don't edit the engine.** Phase 2+ signals should arrive as a new `ICandidateRanker`.

### Load-bearing details that look innocuous

- **Hook subscription order.** `BarInputRouter` must subscribe to the keyboard hook *before*
  `TypingSession.Attach()` is called. Handlers run in subscription order and the contract is: whatever the
  router suppresses, `TypingSession` skips. Reverse it and Tab resets the word buffer mid-cycle.
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

### UI/UX rules

- The bar must **never take keyboard focus** (`WS_EX_NOACTIVATE`).
- Suggestions and autocorrect are **always disabled in password fields**.
- Respect Windows accessibility: transparency-off / High Contrast → solid surfaces; animations-off →
  instant transitions.
- Motion is spring-based; animations omit `From` so re-triggering continues from the current position.

### Testing expectations

- Prediction primitives are unit-tested; keep them that way.
- UI behaviour is verified end-to-end by driving Notepad and reading text back with `WM_GETTEXT`
  (**not** screenshots — see §12).
- Performance is measured, not assumed.

## 14. Current Task

**Phase 1 of a 7-phase plan to harden and then extend the prediction engine.** The owner supplied
`PHASE_1_SymSpell_Frequency_Hardening.md` (in `C:\Users\wordstrip-dev\Downloads\Worstripe\`, outside
the project). Phase 1 explicitly **excludes** n-grams, personal learning, phrase prediction, neural models
and TSF.

**Done in this session [fact]:**

- `PrefixIndex` replacing the full-vocabulary scan
- `ICandidateRanker` + `FrequencyRanker` with deterministic banded scoring
- `Suggestion` extended additively with `Source` and `Score`
- 61 unit tests, all passing
- Performance harness with before/after numbers

**Measured results [fact]:**

| Metric | Before | After |
|---|---|---|
| Prefix lookup `"a"` | 3710.2 µs | 6.2 µs (**600× faster**) |
| Prefix lookup `"wor"` | 2843.3 µs | 14.7 µs (**193× faster**) |
| Prefix lookup `"intern"` | 3255.1 µs | 4.6 µs (**701× faster**) |
| Frequent-word lookup | 13 147.9 µs | 1.7 µs (after caching) |
| Live suggestions (end-to-end) | — | 44.4 µs/call |
| Autocorrection | — | 274.5 µs/call |
| Dictionary load | — | 185 ms |
| SymSpell index build | — | 6187 ms (background, startup only) |

**Not yet done — this is where to resume:**

1. **Persistent bar** (owner's explicit request, not from the Phase 1 doc). Today the strip disappears
   after every inserted word, which the owner finds visually distracting when typing fast. Desired
   behaviour, modelled on Android Gboard: the strip **stays visible** while typing and only disappears when
   the user clicks elsewhere or leaves a text field. Plus **a setting to switch back** to the current
   per-word behaviour.
   - `PredictionEngine.GetFrequentWords(int)` was added specifically to populate the bar between words and
     is already implemented and tested.
   - **[assumption]** The intended dismissal signal is a mouse click outside the bar; `LowLevelMouseHook`
     already ignores clicks on our own windows, so the controller can treat an outside click as "dismiss".
2. **Ship the Phase 1 installer.** The owner asked for a build to install and use for a few days.
3. Update `README.md` and `installer/READ-ME-FIRST.txt` for the new behaviour.

## 15. Recommended Next Steps

1. **`git init` and make an initial commit.** There is no version history at all; this is the single
   highest-value low-cost action. **[recommendation]**
2. **Implement the persistent bar** + `AppSettings` toggle (default: persistent). Hide on outside click
   and when focus leaves a suggestible control. *[recommendation: a ~1 s timer checking
   `FocusedControlInspector` while visible, so the bar cannot linger after Alt+Tab.]*
3. **Run the regression scripts** and rebuild the installer (`build-release.ps1`), bumping the version in
   **both** `installer/WordStrip.iss` and `src/WordStrip.App/WordStrip.App.csproj`.
4. **Resolve the inert `BackdropBlur` setting** — remove it or reimplement blur.
5. **Only then** consider Phase 2. Do **not** start Phase 2 without being asked; the Phase 1 document says
   so explicitly. The natural entry point is a new `ICandidateRanker` that consumes preceding-word context,
   plus replacing `GetFrequentWords` with bigram-conditioned predictions.

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
6. Continue from the documented current task: implementing the persistent suggestion bar
   with a settings toggle, then shipping a Phase 1 installer.
7. Respect the "load-bearing details" in section 13 — several of them look like harmless
   code but will silently break text insertion or the layout if changed.
8. Whenever a major decision, feature or task status changes, update
   CLAUDE_PROJECT_CONTEXT.md so it stays accurate.

Important: this project has no git repository yet, so there is no undo. Consider running
git init before making substantial changes.
```
