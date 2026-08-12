# Project Context

> Handoff document for continuing WordStrip in a fresh Claude Code session.
> Everything marked **[fact]** was verified by inspecting or running the project on the date below.
> Anything marked **[assumption]** or **[recommendation]** is judgement, not established fact.
> `Unknown` means exactly that — it has not been determined, and what is missing is stated.

## 1. Project Identity

- **Project name:** WordStrip **[fact]** — assembly name `WordStrip`, solution `WordStrip.sln`
- **Project type:** Windows desktop utility — a background tray application with a floating, never-focused
  overlay window. Not a service, not a web app, no server component. **[fact]**
- **One-sentence purpose:** Adds phone-keyboard-style word suggestions and offline autocorrect to the
  physical Windows keyboard, shown in a small floating strip near where you type.
- **Current absolute project path:** `D:\Claude Code\WordStrip` **[fact]**
- **Repository:** Local git repository. **No remote configured** (`git remote -v` is empty), so there is no
  origin to push to and no backup outside this machine. **[fact]**
- **Main branch:** `master` — the only branch. **[fact]**
- **Current branch:** `master`, working tree clean at commit `9f24130`. **[fact]**
- **Version:** 0.10.1 — declared in **two** places that must be bumped together:
  `src/WordStrip.App/WordStrip.App.csproj` and `installer/WordStrip.iss`. **[fact]**
- **Last inspected date:** 2026-08-12

### How the project root was identified

The Claude Code session's working directory is `D:\Claude Code`, which is the **parent**, not the root.
`D:\Claude Code` is itself not a git repository. The real project root is `D:\Claude Code\WordStrip`,
identified by the presence of `WordStrip.sln` and `.git\`, and confirmed by every project reference in the
solution resolving relative to it. **[fact]**

Always pass absolute paths to `dotnet`, or `cd` into the root first.

## 2. Product or Software Description

WordStrip runs in the system tray with no main window. A global low-level keyboard hook watches typing;
a small glass strip shows ranked candidate words near the caret.

**Main workflow:**

1. User types in a supported text field.
2. The strip appears with 3–7 candidates.
3. `Tab` highlights the next candidate (hold to scrub); `Space` inserts the highlighted one; `Esc` puts the
   bar away; clicking a chip inserts directly with no need to press Tab first.
4. On finishing a word (space/punctuation), conservative autocorrect may replace an obvious misspelling.
5. The strip **stays on screen between words** by default, predicting what comes next. Tab cycles those
   predictions too — see §12 item 4 for what that costs.
6. Right-click the tray icon → Settings for theme, word count, phrases, emoji, autocorrect, persistent bar,
   bar thickness, glass tint, animation speed, position, personal words, learning, the optional language
   model, and autostart. Everything applies live with a preview.

**Who uses it:** the project owner and a small circle of friends and family testing preview builds. The
owner's stated motivation is that they mistype often and Windows has no equivalent of the Android/iOS
suggestion row. **[fact — stated by the owner]**

**Privacy posture:** entirely local. **The application makes no network calls of its own** — the single
exception is `NeuralModelStore`, which performs plain HTTPS GETs for public model files, and only when the
user presses a button in Settings having been shown the size, licence and publisher. No telemetry, no
account, no analytics. The strip and personal learning both disable themselves inside password fields
(`ES_PASSWORD`). By default nothing typed is written to disk at all; with learning on, what is written is
*counts* of words, pairs and triples — never text. Everything lives in `%LOCALAPPDATA%\WordStrip` as plain
files the user can read and delete. **[fact]**

## 3. Current Status

- **Overall status:** **Phases 1–6 complete and shipped as 0.10.1, installed and in daily use by the owner.**
  **Phase 7 (TSF migration) is the active task. Stages 0 and 4 — the provider abstraction and the fallback
  machinery — are done, built and tested (327 tests). The TSF service itself has not been started and is
  blocked on a missing C++ toolchain.** See §14. **[fact]**
- **Completed:**
  - Keyboard/mouse hooks, text injection, word-buffer tracking
  - Offline SymSpell + frequency prediction and autocorrect
  - Floating glass strip, 7 themes, selection lens, position indicator
  - Spring-based motion with accessibility fallbacks, plus Apple-style bounce on entrance/exit
  - Settings window with live light/dark preview
  - Single-instance handling, tray icon, autostart, per-user installer + portable exe
  - Phase 1: `PrefixIndex`, `ICandidateRanker`/`FrequencyRanker`, additive `Suggestion` metadata
  - Persistent bar + toggle, `IFocusedControlProvider` seam, end-to-end regression harness
  - Phase 2: trigram/bigram model, contextual ranking, typing history, offline model builder
  - Tab claims the bar whenever it is visible (owner request, reversed the 0.4.0 decision)
  - Phase 3: personal vocabulary with casing preservation and autocorrect protection
  - Phase 4: personal learning with bounding, decay, cold-start ramp, privacy controls
  - Phase 5: multi-word phrases via bounded beam search; emoji suggestions; animation-off
  - **Text insertion by window message rather than synthetic keystrokes** — the real fix for the
    partial-insertion bug, confirmed by the owner. 233 ms → 5.9 ms for a 51-character entry.
  - Phase 6: optional ONNX neural reranking with cascade, async execution, cancellation, stale suppression
  - Crash logging to `%TEMP%\wordstrip_crash.log`
  - **Phase 7 Stage 0: the `ITextContextProvider` abstraction, the keyboard-hook adapter behind it, and
    `SuggestionController` rewired to consume it.** **[fact]**
  - **Phase 7 Stage 4: `CompositeTextContextProvider` — per-call provider selection, demotion of providers
    that throw, and a guarantee that no provider failure can stop typing. In the shipping path.** 327 unit
    tests. **[fact]**
- **In progress:** **Phase 7 — TSF migration.** Stages 0 and 4 complete; Stages 1–3 (the text service
  itself) not started. **[fact]**
- **Blocked:** **The TSF service is blocked on tooling.** This machine has **no C++ toolchain at all** — no
  Visual Studio, no Build Tools, no Windows SDK, no cmake, no msbuild; only the .NET 8 SDK. A TSF text
  service is conventionally a native COM DLL, so this must be resolved before Stage 1.
  **Verified absent twice on 2026-08-12**, after the owner reported installing it both times. No `vswhere`,
  no VS package cache, no Windows SDK, and no `cl` / `clang` / `clang-cl` / `gcc` / `cmake` / `msbuild` /
  `ninja` on `PATH`. The uninstall registry holds only VC++ *redistributables* — those ship with countless
  applications and contain no compiler, so they are the obvious false positive to watch for. **[fact]**

  **Cause found: Windows has a restart pending, and the Visual Studio Installer refuses to run until it is
  cleared.** All three markers are set — `Component Based Servicing\RebootPending`,
  `WindowsUpdate\Auto Update\RebootRequired`, and 10 `PendingFileRenameOperations` entries. KB5121003,
  KB5123304 and KB5120708 all installed on 2026-08-12 while the machine had been up since 2026-08-09
  without rebooting. The bootstrapper bails before writing anything, which is exactly why two attempts left
  no package cache, no installer directory and no log to read. **[fact]**

  Permissions are **not** the problem, which is worth recording so nobody chases it: the account is in the
  local Administrators group and UAC sits at the normal prompt level (`EnableLUA=1`,
  `ConsentPromptBehaviorAdmin=5`), with no Windows Installer restriction policy set. **[fact]**
- **Next priority:** Restart Windows, install the toolchain, verify it, then the Stage 1 spike (§14, §15).

### Documentation debt carried into this session **[fact]**

`README.md` and `installer/READ-ME-FIRST.txt` were **not** updated for 0.9.0/0.10.x. The README still
describes 248 tests and lacks the message-based-insertion rationale, the Phase 6/neural section and the
bounce animation. This document is current; those two are not. **[fact]**

## 4. Directory Structure

Verified tree, build output excluded. **[fact]**

```
D:\Claude Code\WordStrip\
├── WordStrip.sln                     4 projects: App, Core, Neural, Core.Tests (+ NGramBuilder tool)
├── README.md                         Engineering documentation — STALE at 0.8.0, see §3
├── CLAUDE_PROJECT_CONTEXT.md         This file
├── build-release.ps1                 One shot: publish portable exe + build installer
├── .gitignore                        bin/, obj/, publish/, .vs/, *.user, *.suo, .corpus/
│
├── .corpus/                          Downloaded corpus (gitignored, ~47 MB, 61 Gutenberg books +
│                                     symspell_bigrams.txt). Regenerate, never commit.
│
├── assets/
│   ├── dict/frequency_dictionary_en_82_765.txt   SymSpell word+frequency list (MIT)
│   ├── ngram/ngram-2.txt, ngram-3.txt            Generated model, committed (7.25 MB)
│   └── wordstrip.ico                             Multi-resolution app icon
│
├── tools/                            Build-time only; never shipped
│   ├── ngram/Fetch-Corpus.ps1        Downloads Gutenberg texts + SymSpell bigrams
│   └── WordStrip.NGramBuilder/       Counts, blends, prunes, writes the model files
│
├── installer/
│   ├── WordStrip.iss                 Inno Setup 6, per-user install, no UAC. VERSION LIVES HERE TOO.
│   └── READ-ME-FIRST.txt             Tester-facing notes — STALE at 0.8.0, see §3
│
├── publish/                          Build output (gitignored, not source)
│   ├── WordStrip-Setup-0.10.1.exe    70.7 MB
│   ├── portable/WordStrip.exe        79,130,621 bytes, self-contained single file
│   ├── portable/onnxruntime*.lib     2 stray import libraries — see §12 item 16
│   └── READ-ME-FIRST.txt
│
├── src/WordStrip.Core/               NO UI dependencies, NO NuGet dependencies
│   ├── Input/
│   │   ├── NativeMethods.cs          All P/Invoke declarations
│   │   ├── LowLevelKeyboardHook.cs   WH_KEYBOARD_LL, Suppress flag
│   │   ├── LowLevelMouseHook.cs      WH_MOUSE_LL, ignores clicks on our own windows
│   │   ├── TypingSession.cs          Reconstructs the in-progress word + last two words of history
│   │   ├── Win32TextInjector.cs      Message-based insertion, SendInput fallback — READ §13 FIRST
│   │   ├── InjectionLog.cs           Opt-in diagnostics (WORDSTRIP_INJECTLOG)
│   │   ├── ITextInjector.cs          Seam — Phase 7's TSF commit path plugs in here
│   │   ├── KeyTranslator.cs          vkCode → character via layout
│   │   └── WordCommittedEventArgs.cs Carries PrecedingWords snapshotted pre-update
│   ├── Automation/
│   │   ├── FocusedControlInfo.cs     record struct: IsStandardEditControl, IsPasswordField, Caret,
│   │   │                             Handle, IsRichEdit
│   │   ├── FocusedControlInspector.cs Static live Win32 inspection
│   │   └── IFocusedControlProvider.cs Seam over the above, so focus can be faked in tests
│   ├── Personal/
│   │   ├── PersonalWord.cs           Key + display casing + usage; date-only recency
│   │   ├── PersonalVocabularyStore.cs Atomic writes, corruption-tolerant, 5000 cap
│   │   └── PersonalLanguageModel.cs  Learned counts, bounded, decaying, cold-start ramped
│   ├── Prediction/
│   │   ├── FrequencyDictionary.cs    Vocabulary + frequency source (top 60,000 loaded)
│   │   ├── PrefixIndex.cs            Sorted array + binary search; cached top-frequency list
│   │   ├── SymSpellIndex.cs          Delete-variant fuzzy candidates (edit distance 2)
│   │   ├── DamerauLevenshtein.cs     Bounded edit distance
│   │   ├── ICandidateRanker.cs       Ranking abstraction + RankingContext
│   │   ├── FrequencyRanker.cs        Banded deterministic scoring — Phase 1, wrap it, don't edit it
│   │   ├── ContextualRanker.cs       Wraps the above, adds the n-gram signal
│   │   ├── PredictionContext.cs      Partial word, preceding words, sentence-start
│   │   ├── PredictionEngine.cs       GetLiveSuggestions + GetNextWords + WithEmoji
│   │   ├── PhraseGenerator.cs        Bounded beam search, up to 3 words
│   │   ├── EmojiSuggester.cs         Keyword matching, ambiguity refusal, min prefix 3
│   │   ├── EmojiTable.cs             ~300 curated keyword→emoji entries
│   │   ├── Suggestion.cs             Candidate contract (Source, Score, Confidence, WordCount)
│   │   ├── NGram/
│   │   │   ├── NGramFormat.cs        On-disk contract, shared with the builder
│   │   │   ├── NGramTokenizer.cs     Shared tokenizer — drift here silently kills every lookup
│   │   │   └── NGramLanguageModel.cs Tables, stupid backoff, ContextLookup fast path
│   │   └── Neural/                   Dependency-free seam; knows nothing about ONNX
│   │       ├── INeuralReranker.cs    IsReady + ScoreAsync; null return means "no opinion"
│   │       ├── NeuralRerankCoordinator.cs Cascade, sequence numbers, cancellation, ≤25 bonus, 250 ms
│   │       ├── UnavailableNeuralReranker.cs Null object
│   │       ├── NeuralModelCatalog.cs Verified descriptor: name, publisher, licence, sizes, URLs
│   │       └── NeuralModelStore.cs   The ONLY code in WordStrip that touches the network
│   ├── Text/                         ← PHASE 7. The input-mechanism abstraction
│   │   ├── TextContext.cs            The snapshot the prediction layer consumes, + TextContextSource enum
│   │   ├── ITextContextProvider.cs   The seam a TSF provider will implement alongside the hook
│   │   ├── KeyboardHookTextContextProvider.cs  The existing path as a provider. Pure adapter, no logic
│   │   └── CompositeTextContextProvider.cs     Stage 4: picks a provider per call, demotes broken ones,
│   │                                 never lets a provider failure stop typing. In use with one provider
│   ├── Suggestions/
│   │   ├── SuggestionController.cs   The only class the UI talks to; persistent-bar state machine.
│   │   │                             Consumes ITextContextProvider — knows nothing about hooks
│   │   └── SuggestionUpdate.cs       Render contract (+ IsIdle)
│   ├── Settings/                     AppSettings, AppSettingsStore, BarTheme, BarPosition, BackdropBlur
│   │   └── UserDataLocation.cs       One place decides where user data lives; WORDSTRIP_DATA_DIR
│   └── Platform/AutostartManager.cs  HKCU Run key
│
├── src/WordStrip.Neural/             ONNX isolated here so Core stays dependency-free
│   ├── WordStrip.Neural.csproj       Microsoft.ML.OnnxRuntime 1.20.1, Microsoft.ML.Tokenizers 1.0.1
│   └── OnnxNeuralReranker.cs         TryLoad never throws; discovers input_ids, attention_mask,
│                                     position_ids, logits name and past_key_values layer count
│
├── src/WordStrip.App/                WPF, net8.0-windows, AssemblyName=WordStrip
│   ├── App.xaml.cs                   Composition root — subscription order is load-bearing
│   ├── SingleInstance.cs             Mutex + "show settings" signal
│   ├── Coordination/BarInputRouter.cs Tab/Space/Enter/Esc handling
│   ├── UI/
│   │   ├── SuggestionBarWindow.xaml(.cs)  The floating strip
│   │   ├── SettingsWindow.xaml(.cs)       Settings + live preview
│   │   ├── SettingsViewModel.cs           Including the neural download/delete commands
│   │   ├── Theming/ThemeCatalog.cs        THE SEVEN THEMES live here and nowhere else
│   │   ├── Theming/ThemeTokens.cs         Token contract
│   │   ├── ThemeBrushes.cs                Tokens → frozen brushes
│   │   ├── GlassPlate.cs / SelectionLens.cs   Custom render-only elements
│   │   ├── GlassMetrics.cs / MotionProfile.cs / SpringEase.cs
│   │   ├── SquircleGeometry.cs             Continuous-curvature corners
│   │   ├── BackgroundProbe.cs              Screen-luminance sampling
│   │   ├── SystemAppearance.cs             Accessibility preferences
│   │   └── FrameProbe.cs                   Opt-in frame-time diagnostic
│   ├── Interop/GlassWindowBehavior.cs      No-activate, no-taskbar window
│   ├── Tray/TrayIconController.cs          WinForms NotifyIcon (the only WinForms use)
│   └── app.manifest                        PerMonitorV2 DPI awareness
│
├── tests/WordStrip.Core.Tests/       xUnit, 327 tests
│   ├── TestVocabulary.cs             Small hand-written vocabulary + fixture
│   ├── TestLanguageModel.cs          Hand-written n-gram fixture
│   ├── PrefixCompletionTests.cs / FuzzyMatchingTests.cs / AutocorrectionTests.cs
│   ├── RankingTests.cs / PrefixIndexTests.cs
│   ├── SuggestionControllerTests.cs  Persistent-bar state machine
│   ├── NGramLanguageModelTests.cs / ContextualPredictionTests.cs / TypingHistoryTests.cs
│   ├── PersonalVocabularyTests.cs (36) / PersonalLearningTests.cs (28)
│   ├── TextInjectionTests.cs (10)    Asserts the batch that WOULD be sent, via InternalsVisibleTo
│   ├── PhraseSuggestionTests.cs (19) / EmojiSuggestionTests.cs (28)
│   ├── NeuralRerankTests.cs (15)     Cascade, staleness, cancellation — no model needed
│   ├── OnnxRerankerTests.cs          SKIPPED unless WORDSTRIP_TEST_MODEL_DIR is set
│   ├── TextContextTests.cs (34)      Phase 7. Also covers controller paths that were unreachable
│   │                                 before a provider could be faked — see its class comment
│   └── PerformanceTests.cs           Timings against the real vocabulary and the real model
│
└── tests/regression/                 End-to-end, drives a real Win32 edit control
    ├── Verify-PersistentBar.ps1      9 checks; see §12 for the traps it took to get right
    └── TestTarget.ps1                Throwaway window with a real EDIT or RICHEDIT50W control
```

## 5. Technology Stack

| Item | Version / detail | Source |
|---|---|---|
| Language | C# 12 | **[fact]** |
| Runtime | .NET 8, `net8.0-windows` on every project | **[fact]** |
| SDK installed | 8.0.423 | **[fact]** `dotnet --version` |
| UI framework | WPF | **[fact]** |
| Secondary UI | WinForms — **only** for the tray `NotifyIcon` | **[fact]** |
| Neural runtime | `Microsoft.ML.OnnxRuntime` 1.20.1, `Microsoft.ML.Tokenizers` 1.0.1 | **[fact]** |
| Tests | xUnit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0 | **[fact]** |
| Installer | Inno Setup 6 (`ISCC.exe` at `%LOCALAPPDATA%\Programs\Inno Setup 6`) | **[fact]** |
| OS (dev machine) | Windows 11 Pro, build 10.0.26200 | **[fact]** |
| Display (dev machine) | 1920×1080 at **150% scaling** — matters for capture/testing | **[fact]** |
| Dictionary | SymSpell `frequency_dictionary_en_82_765.txt` (MIT), top 60,000 loaded | **[fact]** |
| Language model | Own text format, built from Project Gutenberg (public domain) + SymSpell bigrams (MIT) | **[fact]** |
| Optional neural model | DistilGPT2 int8 ONNX, Apache 2.0, 227 MB, downloaded by the user only | **[fact]** |
| Databases | **None.** Plain JSON and text files | **[fact]** |
| APIs / external services | **None at runtime**, except the user-initiated model download from huggingface.co | **[fact]** |

**`WordStrip.Core` has zero NuGet dependencies and this is deliberate** — it is why the prediction stack is
predictable and why a broken package cannot take the app down. ONNX lives in `WordStrip.Neural` behind the
`INeuralReranker` seam. **[fact]**

⚠️ **The app makes essentially no network calls, but the *build* does.** `tools\ngram\Fetch-Corpus.ps1`
downloads ~47 MB of corpus text to regenerate the model. That is a developer step. The model is committed
under `assets\ngram\` and embedded in the exe. **[fact]**

## 6. Architecture and Data Flow

```
keystroke
   ↓
LowLevelKeyboardHook (WH_KEYBOARD_LL)
   ↓  (subscription order is load-bearing — see §13)
BarInputRouter ── consumes Tab/Space/Enter/Esc, sets e.Suppress
   ↓
TypingSession ── skips suppressed keys; rebuilds the in-progress word; keeps last two words
   ↓  CurrentWordChanged / WordCommitted / BufferReset
SuggestionController ── the only class the UI talks to
   ├→ IFocusedControlProvider  (text field? password box? Edit or RichEdit? handle? caret?)
   ├→ PredictionEngine
   │     ├→ PrefixIndex + SymSpellIndex + PersonalVocabularyStore  (candidate generation)
   │     ├→ PhraseGenerator  (beam search over NGramLanguageModel)
   │     ├→ ContextualRanker → FrequencyRanker  (ordering)
   │     └→ EmojiSuggester  (at most one, placed last by policy)
   ├→ NeuralRerankCoordinator  (async, cancellable, skipped when confident) → OnnxNeuralReranker
   ├→ PersonalLanguageModel.Learn  (gated on the same focus check as suggesting)
   └→ ITextInjector (Win32TextInjector) — deferred via postToMessageLoop
   ↓  SuggestionsChanged(SuggestionUpdate{ Suggestions, Caret, IsIdle })
SuggestionBarWindow ── renders; never takes keyboard focus
```

**Key data-flow rules:**

- Text injection **never** happens inside the hook callback. `SuggestionController` takes a
  `postToMessageLoop` delegate and routes every replacement through it. Injecting from inside the hook
  either gets discarded (when the key is suppressed) or interleaves with the in-flight keystroke and
  corrupts the text. **[fact — this was a real bug, fixed]**
- **Insertion prefers window messages over synthetic input.** When focus is a non-password Edit/RichEdit
  with a real handle, `Win32TextInjector` posts `WM_CHAR`/`WM_KEYDOWN` straight to the control. Otherwise it
  falls back to chunked `SendInput`. See §13 for why the deletion encoding branches on `IsRichEdit`.
- The prediction layer contains **no UI code** and the UI contains no prediction logic.
- Neural reranking is a **cascade, not a stage**: it is skipped entirely when the statistical top candidate
  is already confident (`Confidence >= 0.62`), runs asynchronously with a 250 ms timeout, and a result that
  arrives after the context moved on is discarded by sequence number.
- Settings are a single shared mutable `AppSettings` instance; the settings window mutates it and calls back
  into the bar, so changes apply on the next keystroke with no event plumbing.

**Where Phase 7 fits:** the brief's `ITextContextProvider` sits between the Windows input layer and
`SuggestionController`, replacing the direct `TypingSession` + `FocusedControlInspector` coupling with an
abstraction that either the hook path or a TSF path can satisfy. `ITextInjector` is already the seam for
TSF commits. `PredictionEngine` and below must not change at all.

## 7. Setup and Installation

```powershell
winget install --id Microsoft.DotNet.SDK.8 -e
```

```powershell
winget install --id JRSoftware.InnoSetup -e
```

```powershell
dotnet build "D:\Claude Code\WordStrip\WordStrip.sln"
```

**Phase 7 only — not yet installed on this machine, and needed before the TSF service can be built.** A TSF
text service is conventionally a native COM DLL, and nothing here can compile one today: `vswhere`, the
Windows SDK registry key, `cmake` and `msbuild` are all absent. This is a multi-gigabyte download and
requires administrator rights, so run it interactively rather than unattended:

```bash
winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override "--quiet --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

There are **no secrets, API keys, connection strings or `.env` files** in this project, and none are
required. Nothing in §9 is a secret. **[fact]**

## 8. Run, Build, Test, and Debug Commands

**IMPORTANT — the app locks its own exe.** Stop the running process *before* building, or the build fails
with MSB3027 "file is locked":

```bash
powershell -c "Stop-Process -Name 'WordStrip*' -Force -ErrorAction SilentlyContinue"
```

Build the whole solution:

```bash
dotnet build "D:/Claude Code/WordStrip/WordStrip.sln" -c Release
```

Run the app (tray only — no main window appears):

```bash
"D:/Claude Code/WordStrip/src/WordStrip.App/bin/Release/net8.0-windows/WordStrip.exe"
```

Open the settings window directly (also what the Start Menu shortcut does):

```bash
"D:/Claude Code/WordStrip/src/WordStrip.App/bin/Release/net8.0-windows/WordStrip.exe" --settings
```

Run all unit tests (327):

```bash
dotnet test "D:/Claude Code/WordStrip/tests/WordStrip.Core.Tests/WordStrip.Core.Tests.csproj"
```

Print the performance measurements (needs the detailed logger to see the numbers):

```bash
dotnet test "D:/Claude Code/WordStrip/tests/WordStrip.Core.Tests/WordStrip.Core.Tests.csproj" --logger "console;verbosity=detailed" --filter "FullyQualifiedName~PerformanceTests"
```

Run the ONNX tests, which skip themselves unless pointed at a downloaded model:

```bash
powershell -c "$env:WORDSTRIP_TEST_MODEL_DIR='$env:LOCALAPPDATA\WordStrip\model'; dotnet test 'D:\Claude Code\WordStrip\tests\WordStrip.Core.Tests\WordStrip.Core.Tests.csproj' --filter 'FullyQualifiedName~OnnxRerankerTests'"
```

End-to-end regression: 9 checks against a real Win32 edit control. **Takes over the keyboard and the
foreground for about a minute** — do not run it while typing something else:

```bash
powershell -File "D:/Claude Code/WordStrip/tests/regression/Verify-PersistentBar.ps1"
```

Same, against a RichEdit target at a slower typing rate (a plain `EDIT` processes input too synchronously to
expose ordering races):

```bash
powershell -File "D:/Claude Code/WordStrip/tests/regression/Verify-PersistentBar.ps1" -ControlClass RichEdit -PerKeyMs 25
```

Regenerate the n-gram model. Downloads ~47 MB of corpus the first time; about a minute to build:

```bash
powershell -File "D:/Claude Code/WordStrip/tools/ngram/Fetch-Corpus.ps1"
```

```bash
dotnet run --project "D:/Claude Code/WordStrip/tools/WordStrip.NGramBuilder" -c Release
```

Produce `publish\portable\WordStrip.exe` and `publish\WordStrip-Setup-<ver>.exe`:

```bash
powershell -File "D:/Claude Code/WordStrip/build-release.ps1"
```

Silent in-place upgrade of a built installer. Same AppId, so it replaces the existing install and
**preserves settings**. Do **not** verify by uninstalling and reinstalling — uninstall deletes
`%LOCALAPPDATA%\WordStrip` (§9):

```bash
powershell -c "Start-Process 'D:\Claude Code\WordStrip\publish\WordStrip-Setup-0.10.1.exe' -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/TASKS=startupicon' -Wait"
```

**Debugging aids — all opt-in via environment variable, all off by default:**

| Variable | Effect |
|---|---|
| `WORDSTRIP_INJECTLOG=1` | `InjectionLog` writes every replacement to `%TEMP%\wordstrip_injection.log`: `via=`, `chunks=`, `backspaces=`, `chars=`, `events=`, `inserted=`, `ms=`, `focus=`, `text=[...]`. **This is what finally diagnosed the insertion bug** — reach for it before reasoning. **[fact]** |
| `WORDSTRIP_FRAMELOG=1` | `FrameProbe` writes per-frame intervals to `%TEMP%\wordstrip_frames.log` |
| `WORDSTRIP_DATA_DIR=<path>` | Relocates **all** user data. Used by the regression harness to seed a known vocabulary without touching the real one |
| `WORDSTRIP_TEST_MODEL_DIR=<path>` | Points `OnnxRerankerTests` at a downloaded model; without it they skip |

**Crash log — always on.** Unhandled dispatcher and app-domain exceptions append to
`%TEMP%\wordstrip_crash.log`. A tray app has no console and nowhere to show a dialog, so without this an
unhandled exception is untraceable — Windows Error Reporting gives only `0xe0434352`. **[fact — this is the
only reason the 0.10.0 Settings crash was diagnosable.]**

## 9. Configuration and Environment Variables

**No secrets exist in this project and none are required.** No API keys, no tokens, no certificates, no
`.env` file, no connection strings, no ports, no URLs to configure. **[fact]**

| Path | Kind | Purpose |
|---|---|---|
| `%LOCALAPPDATA%\WordStrip\settings.json` | File | User settings, written by the app |
| `%LOCALAPPDATA%\WordStrip\personal-vocabulary.json` | File | Phase 3: the user's own words, casing, usage |
| `%LOCALAPPDATA%\WordStrip\personal-language-model.json` | File | Phase 4: learned counts. Absent unless learning is on |
| `%LOCALAPPDATA%\WordStrip\model\` | Folder | Phase 6: `model.onnx`, `vocab.json`, `merges.txt`. Absent unless downloaded |
| `%LOCALAPPDATA%\Programs\WordStrip\` | Folder | Install location (per-user, no UAC) |
| `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WordStrip` | Registry | Autostart entry |
| `%TEMP%\wordstrip_crash.log` | File | Always on |
| `%TEMP%\wordstrip_injection.log`, `wordstrip_frames.log` | File | Opt-in only |

**The only outbound URL in the whole product**, and only when the user presses Download:
`https://huggingface.co/onnx-community/distilgpt2-ONNX/resolve/main/` — three files: `onnx/model_int8.onnx`
(236,714,483 bytes, saved locally as `model.onnx`), `vocab.json` (798,156), `merges.txt` (456,318). Sizes
verified against the publisher, not estimated. **[fact]**

`settings.json` shape — this is the **actual current content on the dev machine** **[fact]**, which is also
a useful record of how the owner runs it:

```json
{
  "SuggestionCount": 7,          // 3..7
  "GlassTint": 0.95,             // 0.15..0.95
  "BarScale": 1.4,               // 0.7..1.4
  "MotionSpeed": 2.5,            // 0.5..2.5; the maximum means animation OFF entirely
  "Theme": 5,                    // BarTheme enum 0..6
  "BackdropBlur": 3,             // Auto=3 — this setting is INERT, see §12 item 12
  "BarPosition": 2,              // 0 bottom, 1 follow caret, 2 top
  "AutocorrectEnabled": true,
  "PersistentBar": true,
  "PersonalLearningEnabled": true,   // default is FALSE; the owner opted in
  "EmojiSuggestionsEnabled": true,
  "PhraseSuggestionsEnabled": true,
  "NeuralRerankingEnabled": true,    // default is FALSE; the owner opted in
  "StartWithWindows": true
}
```

Missing keys fall back to defaults, so an older `settings.json` needs no migration. **[fact — verified on
the 0.3.0 → 0.4.0 in-place upgrade]**

⚠️ The installer's `[UninstallDelete]` removes `%LOCALAPPDATA%\WordStrip` **entirely**, so an
uninstall/reinstall cycle destroys settings, personal vocabulary, learned data **and the 227 MB model**,
while an in-place upgrade preserves them. Back the folder up before any install testing. **[fact]**

## 10. Important Files

Inspect these first, roughly in this order:

| File | Why it matters |
|---|---|
| `src/WordStrip.Core/Input/Win32TextInjector.cs` | The most hard-won file in the project. Four wrong fixes preceded the current one; its comments record why deletions are backspaces and why the encoding branches on RichEdit. **Read before touching anything about insertion.** |
| `src/WordStrip.App/App.xaml.cs` | Composition root. Hook subscription order here is load-bearing |
| `src/WordStrip.Core/Suggestions/SuggestionController.cs` | The seam between input, prediction and UI; owns the persistent-bar state machine. **Phase 7's context abstraction lands here** |
| `src/WordStrip.App/Coordination/BarInputRouter.cs` | Decides when the bar may claim keys — read alongside `SuggestionUpdate.IsIdle` |
| `src/WordStrip.Core/Automation/FocusedControlInspector.cs` | How the app decides a surface is typeable. **This is exactly what TSF would replace** |
| `src/WordStrip.Core/Prediction/PredictionEngine.cs` | Orchestrates both modes: `GetLiveSuggestions` and `GetNextWords` |
| `src/WordStrip.Core/Prediction/Neural/NeuralRerankCoordinator.cs` | The async/cancellation/staleness pattern to copy if Phase 7 needs another async stage |
| `tests/regression/Verify-PersistentBar.ps1` | How input behaviour is actually verified; its comments record several dead ends |
| `src/WordStrip.Core/Prediction/NGram/NGramLanguageModel.cs` | The contextual model, backoff, and the `ContextLookup` fast path |
| `src/WordStrip.Core/Prediction/ContextualRanker.cs` | How context is weighed against frequency, and why it is capped |
| `src/WordStrip.Core/Prediction/FrequencyRanker.cs` | Phase 1's banded scoring; wrap it, never edit it |
| `src/WordStrip.App/UI/Theming/ThemeCatalog.cs` | All seven themes; the only place visual differences live |
| `src/WordStrip.Core/Input/TypingSession.cs` | Word-buffer rules and why keys are skipped |
| `tests/WordStrip.Core.Tests/PerformanceTests.cs` | Current performance baseline |
| `README.md` | Engineering rationale and bug post-mortems — **but stale at 0.8.0** |

## 11. Recent Work

**Current session, second half (Phase 7 Stage 4): the fallback machinery.** **[fact]**

| File | Change | Reason |
|---|---|---|
| `Text/CompositeTextContextProvider.cs` | **New.** Per-call provider selection, permanent demotion of any provider that throws, event filtering so only the active provider publishes | The brief states this requirement in three separate sections. Built **before** the provider it protects against, because a fallback added afterwards around something already known to be flaky is a fallback nobody trusts |
| `App.xaml.cs` | Composes `KeyboardHookTextContextProvider` → `CompositeTextContextProvider` → controller, and disposes both | The composite is in the shipping path **with only one provider**, so the fallback code is exercised in every build rather than first meeting reality on the day TSF lands |
| `tests/CompositeTextContextTests.cs` | **New**, 22 tests (305 → 327) | Includes two end-to-end ones: suggestions keep working when the preferred provider dies mid-sentence, and an accepted word still reaches the document when it is broken from the start |

Design points worth not re-litigating:

- **Selection is per call, not per session.** TSF availability follows the focused application and alt-tabbing
  raises no event, so caching the decision would strand the user on the fallback for the rest of the session.
- **`NoteTextInserted` goes to every provider, not just the active one.** The hook filters injected keystrokes
  out of its shadow buffer, so a word accepted while another provider was driving would be invisible to it,
  and switching back mid-sentence would lose that word.
- **Demotion is permanent for the process.** Retrying a throwing provider would pay for the same exception
  dozens of times a second while someone types. A cooldown is the change to make if a transient failure ever
  matters — not removing the demotion.

**Current session, first half (Phase 7 Stage 0): the input-mechanism abstraction.** **[fact]**

| File | Change | Reason |
|---|---|---|
| `Text/TextContext.cs` | **New.** `TextContext` snapshot + `TextContextSource` enum | The value the prediction layer consumes, with no Windows types in it beyond a four-integer caret rect. Deliberately has **no "surrounding text" member** so a future TSF provider cannot quietly fill it with a screenful of someone's document |
| `Text/ITextContextProvider.cs` | **New.** The seam | What a TSF provider will implement *alongside* the hook, not instead of it. `IsAvailable` is what lets a provider drop out per-application and hand back to the fallback |
| `Text/KeyboardHookTextContextProvider.cs` | **New.** The existing path as a provider | Pure adapter, contributes no logic of its own — the fallback had to keep behaving identically, so nothing was rewritten |
| `Suggestions/SuggestionController.cs` | Consumes `ITextContextProvider` instead of `TypingSession` + `IFocusedControlProvider` | Acceptance criteria 2 and 4: the controller can no longer tell which input mechanism it has. The old constructor is kept as an overload that builds the hook provider, so the composition root and all 18 existing tests were untouched |
| `tests/TextContextTests.cs` | **New**, 34 tests (271 → 305) | Covers the value type, the adapter, and **controller paths that were previously untestable** — `SuggestionControllerTests` carried a comment saying so, because `TypingSession` raises events from a hook callback that cannot be synthesised. A fake provider reaches them |

All 271 pre-existing tests passed unchanged, which is the evidence that the refactor altered no behaviour.
Solution builds clean with 0 warnings; the app was launched and ran without a crash-log entry.

**Previous session (0.9.0 → 0.10.1): the insertion fix, Phase 6, and a crash.** **[fact]**

| File | Change | Reason |
|---|---|---|
| `Input/Win32TextInjector.cs` | Post `WM_CHAR`/`WM_KEYDOWN` directly to the focused control; `SendInput` kept as fallback | The real fix for partial insertion. 233 ms → 5.9 ms for 51 characters |
| `Input/InjectionLog.cs` | **New.** Opt-in log of every replacement | Three fixes had failed on reasoning alone; this produced the evidence |
| `Automation/FocusedControlInfo.cs` | Added `Handle` and `IsRichEdit` | Message-based insertion needs the target handle and must encode deletions differently per control class |
| `Prediction/Neural/*` | **New**, 5 files in Core | Phase 6 seam, cascade, catalog and download — all dependency-free |
| `src/WordStrip.Neural/` | **New project** | ONNX kept out of Core so Core stays dependency-free |
| `Neural/OnnxNeuralReranker.cs` | **New.** Graph discovery incl. `position_ids`, one forward pass scores the whole list | DistilGPT2 needs `position_ids`; scoring per candidate would cost 5× |
| `Settings/AppSettings.cs` | `NeuralRerankingEnabled`, default off | Separate from the download so it can be switched off without discarding 227 MB |
| `UI/SettingsWindow.xaml` | "Smarter suggestions (optional)" card | Licence, size and publisher shown **before** anything is fetched |
| `UI/SettingsWindow.xaml` | `ProgressBar Value` → `Mode=OneWay` | **Shipped a crash in 0.10.0.** `ProgressBar.Value` binds two-way by default and `NeuralProgress` is read-only; WPF throws and the tray app vanishes |
| `App.xaml.cs` | Crash logging; `LoadNeuralModelAsync` before `StartSuggestionEngine` | Without the log the crash left only `0xe0434352` |
| `UI/SuggestionBarWindow.xaml.cs` | Apple-style bounce on entrance and exit | Owner request |
| `tests/NeuralRerankTests.cs`, `OnnxRerankerTests.cs` | **New**, 15 + skippable | Cascade and staleness are testable without a model; the model tests skip by default |

**Session before that (Phase 5 + emoji, 0.7.0–0.8.0):** `PhraseGenerator` (bounded beam search, mean log
probability per word), `EmojiSuggester` + ~300-entry table, animation-off at the far end of the speed
slider, the first (wrong) single-`SendInput`-batch insertion fix, and `WORDSTRIP_DATA_DIR`.

**Session before that (Tab fix + Phases 3 and 4, 0.6.0):** `BarInputRouter` reduced to a single
`_isBarActive` so Tab works on idle predictions; `PersonalVocabularyStore`, `PersonalWord`,
`PersonalLanguageModel`; personal completions merged into `PredictionEngine` and protected from autocorrect;
bounded personal and learned ranking bonuses; "Your words" and "Learning" settings cards; 64 new tests.

**Session before that (Phase 2, 0.5.0):** `NGramLanguageModel` with stupid backoff, `NGramTokenizer` and
`NGramFormat` shared with the builder, `PredictionContext`, `ContextualRanker` wrapping `FrequencyRanker`,
`PredictionEngine.GetNextWords`, typing history in `TypingSession`, `tools\ngram\` and
`tools\WordStrip.NGramBuilder`, 47 new tests.

**Earlier:** persistent bar + `IFocusedControlProvider` + regression harness (0.4.0); Phase 1 prediction
hardening (`PrefixIndex`, `ICandidateRanker`, `FrequencyRanker`); the 7-theme token system.

**Commit history** (`master`, 15 commits, tags v0.4.0 … v0.10.1) **[fact]**:

```
9f24130 Fix a crash opening Settings, and record crashes when they happen
afa1d93 Phase 6: optional local neural reranking, and a real fix for insertion
012978a Insert text by message instead of by pretending to be a keyboard
c32856c Phase 6 foundation: neural reranking seam, cascade and concurrency
b5b8ffe Injection diagnostics, chunked batches, and a proper entrance animation
e6c44a8 Phase 5: multi-word phrases, plus emoji suggestions
3d454eb Send deletions and replacement text as one SendInput batch
78d134a Phases 3 and 4: personal vocabulary and privacy-first learning
558788d Tab now cycles whenever the bar is showing suggestions
```

## 12. Known Problems

### Limitations (by design, not bugs)

1. **Works only in classic Win32 `Edit`/`RichEdit` controls.** Notepad and most desktop dialogs work.
   **Chrome, Edge, all browsers, Electron apps (Slack, VS Code, Discord) and Microsoft Office do not.**
   This is the single biggest gap and **is precisely what Phase 7 exists to address.** **[fact]**
2. **English only** — one bundled dictionary.
3. **Tab belongs to the bar whenever the bar is visible.** Since 0.6.0, at the owner's request. The cost is
   that Tab will not indent or move between dialog fields while the strip is up — which, with the persistent
   bar on, is most of the time you are in a text field. Esc releases it until the next keystroke. **[fact]**
   *[recommendation: this reversed the opposite decision made in 0.4.0, on one report. If it grates, a
   modifier chord is the fallback and `BarInputRouter` is the only file involved.]*
4. **The corpus skews literary.** Mostly 19th- and early-20th-century novels, so ordinary English is well
   covered and modern, technical or workplace phrasing is not. The model has never seen "pull request".
   Phrases inherit the same register — "thank you" can suggest "sir said the". Grammatical, just dated.
   Personal learning is the practical mitigation. **[fact]**
   *[recommendation: a modern conversational corpus would help more than any further tuning.]*
5. **Autocorrect cannot correct *into* a personal word.** Personal words are protected from correction and
   offered as completions, but `SymSpellIndex` is built from the general dictionary at startup and never
   rebuilt, so "githb" will not become "GitHub". **[fact]**
6. **Neural reranking only activates at startup.** `LoadNeuralModelAsync` runs once in `OnStartup`, so
   downloading the model or ticking the checkbox does nothing until WordStrip is restarted. The settings
   window says so ("Downloaded. Restart WordStrip to start using it."), so this is disclosed rather than
   hidden. **[fact — confirmed on the running process: the model is downloaded and the setting is on, but
   `onnxruntime.dll` is not loaded into the 21-minute-old process.]**
7. **The neural signal is coarse.** First-token scoring on an 82M model quantised to int8 reliably rejects
   nonsense and clearly reads context (it scores "you" ~7.5 nats higher after "thank" than after "looking"),
   but does **not** reliably make finer calls — after "i am really looking" it narrowly prefers "you" to
   "forward". The tests assert what it can actually do, not what would be nice. **[fact]**

### Open bugs and technical debt

8. **The word buffer can miss a keystroke if the UI thread is briefly busy.** A low-level keyboard hook is
   only called if the thread that installed it is free to service it; if not, Windows times the hook out and
   the app never sees that key, while the target application still receives it. The buffer is then one
   character short and a replacement lands with the first typed letter surviving in front of it
   ("aAlexandra Fairbourne Reed"). **[fact — reproduced in the harness roughly one run in six]**
   *[**Mitigated, not fixed.** Message-based insertion cut the busy window from 80–233 ms to 1–2 ms, so it
   now only reproduces when typing resumes within a few hundred milliseconds of an insertion, which the
   harness does and people mostly do not. The real fix is to verify the text before the caret against the
   buffer before replacing, and correct the deletion count when they disagree. **Phase 7's TSF context would
   make this bug structurally impossible where TSF is available**, since the surrounding text would be read
   from the document rather than reconstructed.]*

   **Measured 2026-08-12, both sides of the Phase 7 refactor** — 4 harness runs each, plain `EDIT` at the
   default 90 ms pace. Post-refactor build: 1 failure in 4 (`how are` → `how are  you`, a doubled space).
   Pre-refactor 0.10.1: 1 failure in 4 (personal entry inserted incomplete). **Same rate, different check
   each time, which is the signature of this timing bug rather than of a regression.** **[fact]** Which
   check fails carries no information — it depends on whichever keystroke happens to be dropped.
   *[recommendation: four runs each is a small sample. If this rate ever needs to be a real number rather
   than a reassurance, the harness needs a repeat flag and a tally.]*
9. **Memory: ~524 MB private with no neural model, and ~1104 MB private with it.** Both measured on the
   running 0.10.1 process on 2026-08-12. **[fact]** The n-gram model accounts for only 26.3 MB of the
   baseline. *[assumption: the bulk of the baseline is the SymSpell edit-distance-2 index, which has existed
   since Phase 1 and has never been profiled. Unverified.]*
   **The model's ~580 MB delta is broadly consistent with the "about 400 MB" the settings card advertises,
   but the card states a delta while a user will read a total — and the total is over a gigabyte.** That is a
   lot for a keyboard utility and should be understood, and probably reworded, before this goes to anyone
   beyond the owner's testers.
10. **`PerformanceTests.MeasureLanguageModelPerformance` is load-sensitive and can fail spuriously.**
    Observed **[fact]**: on a busy machine the same phrase-generation call measured 548.6 µs at one call site
    and 2612.4 µs at the assertion 18 lines later, tripping the 2000 µs threshold. Re-run on an idle machine:
    passes. **A failure here is not automatically a regression — re-run before investigating.**
    *[recommendation: take the best of three runs rather than one, or the test will cry wolf again.]*
11. **`BackdropBlur` setting is inert.** Switching to per-pixel alpha (`AllowsTransparency=True`) made real
    DWM Mica/Acrylic impossible, because they cannot apply to a layered window. The UI control was removed
    but the enum and `AppSettings.BackdropBlur` remain and are read nowhere meaningful. **[fact]**
12. **Startup index build takes ~6.2 s** (SymSpell) **plus ~1.5–3.4 s** (n-gram load), plus **3.0–4.4 s** if
    the neural model is enabled. All on background threads so the tray icon appears immediately, but first
    run feels slow. **[fact]**
13. **Autostart has two sources of truth that can disagree.** The installer's `startupicon` task writes the
    `HKCU\...\Run` value directly, while the settings window writes both the registry and
    `AppSettings.StartWithWindows`. They can drift, and the checkbox then shows the wrong state. **[fact]**
    *[recommendation: have `AppSettings` read the registry as the source of truth rather than caching it.]*
14. **Unsigned binaries** — SmartScreen warns on first run for testers. Documented in `READ-ME-FIRST.txt`.
15. **`README.md` and `installer/READ-ME-FIRST.txt` are stale at 0.8.0.** See §3. **[fact]**
16. **The portable output is no longer exactly one file.** `publish/portable/` now also contains
    `onnxruntime.lib` (2,124 bytes) and `onnxruntime_providers_shared.lib` (2,314 bytes), leaked out of the
    ONNX Runtime package. `build-release.ps1` warns about this but does not fail. They are import libraries,
    irrelevant at runtime, so copying `WordStrip.exe` alone still works — but the invariant the check exists
    to protect is now technically violated. **[fact]** *[recommendation: exclude `*.lib` from the check or
    from the publish output, rather than letting the warning become background noise.]*

### Testing gotchas discovered the hard way — read before writing UI or input tests

17. **`PrintWindow` cannot capture a DWM backdrop.** It only captures what the app itself draws. Judging
    translucency from a `PrintWindow` grab is meaningless. **[fact]**
18. **PowerShell is DPI-unaware by default.** Call `SetProcessDPIAware()` first or captures are rendered into
    undersized bitmaps and silently cropped. The dev display is at 150%. **[fact]**
19. **`SetForegroundWindow` is silently refused** from a background process. Use the `AttachThreadInput`
    technique, and always verify the foreground window class before sending keystrokes — otherwise test
    input lands in whatever the user is actually using. **[fact]**
20. **Neither Notepad nor WinForms works as an automated typing target.** Windows 11 ships Notepad as a
    packaged single-instance app: `notepad.exe` exits immediately and `MainWindowHandle` is empty. A WinForms
    `TextBox` reports class `WindowsForms10.EDIT.app.0.<hash>`, which does **not** start with `Edit`, so
    `FocusedControlInspector` ignores it and no bar ever appears. `TestTarget.ps1` creates a real `EDIT`
    control with `CreateWindowEx` instead. **[fact — both tried and discarded]**
21. **`SendKeys` types faster than any keyboard and corrupts the result.** Send one key at a time. This is
    the harness outrunning hardware, **not** a product defect. **[fact]**
22. **Cold starts need a warm-up word.** The first replacement after launch pays JIT on the whole injection
    path. The self-contained single-file build is worse, since it also self-extracts. **[fact]**
23. **Choose test misspellings with exactly one plausible correction.** `helo` is one edit from `help` *and*
    `hello`, so the tie falls to frequency and `help` wins — correct behaviour, useless assertion. `teh` →
    `the` is unambiguous. **[fact]**
24. **Keep non-ASCII out of string literals in the PowerShell scripts.** They are saved as UTF-8 with no BOM,
    and Windows PowerShell decodes such a file as Windows-1252 — where an em dash's third byte becomes a
    smart closing quote the parser honours as a string delimiter. It reports as "missing terminator" at the
    *bottom* of the file, hundreds of lines away. Harmless in comments, fatal inside a string. **[fact]**
25. **A bare `EDIT` control has no Ctrl+A.** Select-all comes from the dialog manager, which the test target
    deliberately does not run. Clear it with `EM_SETSEL` + `WM_CLEAR`. **[fact]**
26. **`Start-Process -ArgumentList` does not quote for you.** `D:\Claude Code` has a space in it and breaks
    `-File` unless quoted explicitly. **[fact]**

## 13. Development Instructions

### Standing instructions from the owner

1. Read this file first; treat it as primary context but **verify against the actual files**.
2. Confirm the working directory. The project root is `D:\Claude Code\WordStrip`, not the session's `cwd`.
3. Summarise your understanding, identify the current task and the expected next step.
4. **Inspect relevant files before changing anything.**
5. **Propose a plan before acting.**
6. Do not repeat questions already answered here.
7. Ask only necessary clarification questions.
8. **Do not modify unrelated files.**
9. **Update this file after major changes, decisions, or task-status changes.**

### Architecture rules

- **`WordStrip.Core` must never reference WPF or contain UI logic.** The prediction layer especially.
- **`WordStrip.Core` must stay free of NuGet dependencies.** ONNX lives in `WordStrip.Neural` behind
  `INeuralReranker`. Any future runtime gets the same treatment.
- **Do not expose Windows-specific types to `WordStrip.Core.Prediction`.** Phase 7 restates this; it has been
  true since Phase 1.
- **Theme differences live only in `ThemeCatalog`.** Seven presets over one component, one geometry system,
  one interaction model, one motion system — never seven implementations.
- **Add a ranker; don't edit the engine.** `ContextualRanker` wraps `FrequencyRanker.Score` and leaves it
  untouched. Every later signal has arrived the same way.
- **Grow the context types additively.** `RankingContext` gained `PredictionContext` with a default, so every
  existing call site still compiles and still means what it did. Same for `Suggestion.Source`.

### Load-bearing details that look innocuous

- **Hook subscription order — on both hooks.** `BarInputRouter` must subscribe to the keyboard hook *before*
  `TypingSession.Attach()`. Handlers run in subscription order and the contract is: whatever the router
  suppresses, `TypingSession` skips. Reverse it and Tab resets the word buffer mid-cycle. The mouse hook is
  the same: the controller's `Dismiss()` must subscribe before `Attach()`, or the buffer reset republishes
  the idle list and the bar flashes back on for one frame on every outside click.
- **"Visible" and "owns the keyboard" are different conditions.** `SuggestionUpdate.IsIdle` keeps them apart.
- **`Dismiss()` is sticky on purpose.** It sets a flag that survives until the user types again.
- **Never call `SendInput` from inside the hook callback.** Always via `postToMessageLoop`.
- **Deletions are sent as backspaces, not as a selection — this is a correctness requirement.** Selecting the
  range with `EM_SETSEL` fails intermittently because a *sent* message jumps ahead of *queued* input, so
  autocorrect reads a stale caret and `"teh "` becomes `"he  "` about one time in three. **[fact — measured]**
- **The backspace encoding must branch on `IsRichEdit`.** `WM_CHAR` 0x08 works on `EDIT` and is ignored by
  RichEdit; `WM_KEYDOWN VK_BACK` works on RichEdit and loses characters on `EDIT`, because `TranslateMessage`
  generates a second `WM_CHAR` 0x08 that lands *after* the text. Both were shipped and both were wrong.
- **One `SendInput` call per replacement** on the fallback path — deletions and text together. Windows
  guarantees events inside a single call are not interleaved, and guarantees nothing between two calls.
- **Every ranking bonus stays under the 100-point band gap, and they accumulate.** Context ≤40, personal word
  ≤30, learned usage ≤15, neural ≤25. Add another signal without checking the total and a suggestion could
  outrank a word the user has finished typing.
- **Phrases and emoji are listed explicitly in `FrequencyRanker`'s switch.** The default is the fuzzy band at
  zero, which would silently bury them.
- **`INPUT` struct must include the `MOUSEINPUT` union member** even though it is unused — it sets
  `sizeof(INPUT)` to the 40 bytes x64 requires. Without it, `SendInput` silently rejects everything.
- **Injected-key detection uses a private `dwExtraInfo` marker**, not `LLKHF_INJECTED` (which is set for *any*
  process's SendInput, so relying on it would ignore dictation software and automation tools).
- **`GlassPlate` and `SelectionLens` report zero desired size deliberately.** Consequently **do not put a
  `BitmapCache` on them** — a cache sized from a zero-size element renders nothing.
- **A reappearing bar must clear its selection.** Otherwise the next `Space` replaces a word never chosen.
- **The tokenizer is shared between the offline builder and the running app, and must stay that way.** If the
  two ever disagree about what a token is — a curly apostrophe, a trailing comma, a capital — every lookup
  misses. No error, no crash, just a bar that silently stops predicting.
- **Typing history must be dropped whenever the word buffer is.** Stale context is worse than none: it
  produces confident, specific predictions from words no longer behind the caret.
- **Resolve the n-gram context once per candidate list, not per candidate.** Measured: 161 µs → 484 µs.
- **Don't add raw word frequency on top of a conditional probability.** `P(word | context)` already accounts
  for how common the word is. This is why `ProbabilityWeight` is 8 and not 2.
- **Learning is gated on the same focus check as suggesting.** A field the app cannot positively identify
  must never be learned from — "we're not sure" has to mean "don't record it".
- **`WordCommittedEventArgs.PrecedingWords` is snapshotted before the history updates.**
- **Both personal stores write via a temp file and `File.Replace`.** A corrupt file loads as empty and is
  left on disk for recovery rather than overwritten.
- **Neural results are discarded by sequence number, not by timestamp.** A result for a context the user has
  already typed past must never be applied.
- **XAML bindings resolve at layout, not at compile time.** 271 passing tests did not catch a two-way binding
  against a read-only property; only opening the window did. **Open every window you touch.**

### UI/UX rules

- The bar must **never take keyboard focus** (`WS_EX_NOACTIVATE`).
- Suggestions, autocorrect and learning are **always disabled in password fields**.
- Respect Windows accessibility: transparency-off / High Contrast → solid surfaces; animations-off → instant
  transitions.
- Motion is spring-based; animations omit `From` so re-triggering continues from the current position.
- Anything that costs the user something — disk space, a download, a record of their typing — states the cost
  before it happens and defaults to off.

### Testing expectations

- Prediction primitives are unit-tested; keep them that way.
- UI and input behaviour is verified end-to-end with `tests\regression\Verify-PersistentBar.ps1`, which drives
  a real Win32 `Edit` or `RICHEDIT50W` control and reads text back with `WM_GETTEXT` (**not** screenshots).
  Add a check there for anything a unit test cannot reach; §12 items 17–26 are the traps that cost the most
  time.
- When behaviour in `Core` can't be tested because it reads live Win32 state through a static, **add a seam**
  — `ITextInjector` and `IFocusedControlProvider` are both precedents, and both are what Phase 7 will build on.
- Performance is measured, not assumed.
- Tests that need a 227 MB model skip themselves by default and opt in via `WORDSTRIP_TEST_MODEL_DIR`.

## 14. Current Task

**Phase 7 — move toward the Windows Text Services Framework.** Requested by the owner on 2026-08-12 with the
brief at `C:\Users\wordstrip-dev\Downloads\Worstripe\PHASE_7_Windows_TSF_Migration.md`.

**Stages 0 and 4 are done — everything in the phase that does not need a compiler.** See §11 for the files.
Between them they satisfy acceptance criteria 2 (prediction engine remains Windows-agnostic), 4 (candidate UI
remains independent), 5 (existing fallback remains functional) and 6 (normal typing never depends
exclusively on prediction). **[fact]**

**Stages 1–3 — the text service itself — have not been started, and are blocked on tooling.** This machine
has no C++ toolchain. The owner has approved adding one and reported installing it; verification on
2026-08-12 found it still absent, with no install attempt in the winget logs. See §3 for exactly what was
checked. **[fact]**

Do not attempt to work around this by writing the TIP in managed code without first running the spike below.
"Try it in C# and see" is how a week disappears into CLR-in-arbitrary-host loader problems.

The full 7-phase plan lives in `C:\Users\wordstrip-dev\Downloads\Worstripe\` as `PHASE_1..7_*.md`, outside
the project directory.

### What Phase 7 asks for

**Critical rule, stated first in the brief: do NOT destroy the existing working input path.** The target is
`existing input path + TSF → shared prediction engine`, incrementally, with TSF eventually becoming primary
*where supported*.

- **Abstraction:** an `ITextContextProvider` covering surrounding text, cursor position, selection, current
  word, previous words, commit/replacement, and field context where available. No Windows-specific objects
  may reach `WordStrip.Core.Prediction`.
- **Stage 1:** a TSF proof of concept that registers the text service, receives text/context, and exposes
  basic candidate information. Do **not** replace the current injector yet.
- **Stage 2:** feed real surrounding text to the prediction engine instead of reconstructing it from
  keystrokes, where TSF provides it.
- **Stage 3:** commit candidates through the appropriate TSF mechanism.
- **Stage 4:** keep fallback for applications where TSF is unavailable.
- **Threading:** follow the Windows API contract precisely. Never block a TSF callback with model loading,
  disk I/O or neural inference.
- **Privacy:** pass the minimum context necessary. Do not store documents, upload text, or log keystrokes.
- **UI:** the bar keeps receiving `PredictionCandidate[]`, never TSF objects, so the seven themes stay reusable.
- **Testing matrix required:** Notepad, Windows text controls, Win32, WPF, WinUI/XAML, Chromium/Electron,
  Office if available, elevated apps. Each must be recorded as `works` / `partial` / `unsupported` /
  `fallback`.
- **Measure:** TSF startup, context retrieval, prediction, candidate display, commit latency, memory.
- **Non-goals:** no algorithm redesign, no UI replacement, no cloud, no telemetry, no removing the fallback.
- **Explicit instruction in the brief:** *"Do not claim WordStrip is a full Windows input method until the
  implementation and compatibility testing genuinely justify that claim."*

### Stage 1 spike — results **[fact, 2026-08-12]**

A TSF text service is an **in-process COM server** that Windows loads into every application accepting text.
Three questions had to be answered before designing one. All three were answered **empirically, from the 21
text services already registered on this machine** — no compiler required, which is why this was worth doing
while the toolchain was still blocked. Method: enumerate `HKLM\SOFTWARE\Microsoft\CTF\TIP`, then read each
CLSID's `InprocServer32` under `HKLM\SOFTWARE\Classes\CLSID` and `...\WOW6432Node\CLSID`.

| Question | Answer | Evidence |
|---|---|---|
| Managed or native? | **Native. Do not attempt managed.** | 11 TIPs have a COM server registered. **0 are managed** — not one points at `mscoree.dll` or a `.comhost.dll`. Every IME Microsoft ships (Japanese, Korean ×2, Chinese), plus speech, the touch keyboard and the table text service, is a native DLL |
| Per-user or admin registration? | **Machine-wide. Requires administrator rights.** | **21 registrations under `HKLM`, 0 under `HKCU`.** The COM servers live under `HKLM\SOFTWARE\Classes\CLSID` |
| Visible to the user? | **Yes — a selectable input method, but it can ship switched off** | Visibility comes from `LanguageProfile\<langid>\<profileguid>` carrying `Description`, `IconFile` and `Enable`. The Japanese IME is registered with **`Enable = 0`**: machine-level registration does not force a TIP on, the user opts in. Per-user choice lives in `HKCU\SOFTWARE\Microsoft\CTF\Assemblies` and `SortOrder\AssemblyItem` |

**A fourth constraint turned up that was not on the list, and it is the expensive one:**

- **All 11 register *both* an x64 and a WOW64 32-bit server. Every single one.** A 32-bit host process loads
  the 32-bit TIP; there is no thunking. WordStrip is x64-only today (`-r win-x64`,
  `ArchitecturesAllowed=x64compatible`), so supporting 32-bit applications means **building and shipping a
  second architecture of the TIP**. Dropping 32-bit support is a legitimate choice, but it must be a choice
  rather than a discovery made after the x64 build works. **[fact]**
- **Threading model is `Apartment` (STA) on all of them**, which is the concrete form of the brief's warning
  about following the TSF threading contract.

**What this changes about the plan:**

1. **The installer story breaks.** `installer/WordStrip.iss` is deliberately `PrivilegesRequired=lowest` —
   per-user, no UAC, chosen so testers doing a favour are not asked to elevate (§9). Registering a TIP
   cannot be done that way. Either the installer gains an elevated component, or TIP registration becomes a
   separate opt-in step inside Settings that requests elevation when the user asks for it.
   *[recommendation: the second. It keeps the default install exactly as it is, and it matches the pattern
   already used for the neural model — a capability the user turns on deliberately, having been told the
   cost.]*
2. **Native C++ is confirmed**, not assumed. The `ITextContextProvider` seam is what keeps this contained:
   the DLL only has to gather context and hand it over.
3. **Decide on 32-bit before writing any build script.**

**Still unknown, and needs the compiler:** whether Chromium/Electron and Office actually deliver usable
context in practice. The brief says not to assume this, and nothing in the registry answers it.

## 15. Recommended Next Steps

Ordered.

1. ~~Introduce `ITextContextProvider`~~ and ~~the fallback machinery~~ — **both done.** See §11 and §14.
2. **Restart Windows first.** A pending restart is what blocked two install attempts — see §3. The Visual
   Studio Installer checks for this and exits before doing anything, so retrying without rebooting will fail
   the same silent way a third time. **[fact]**
3. **Then install the C++ toolchain — and verify it afterwards rather than assuming.** Visual Studio Build
   Tools with the "Desktop development with C++" workload and a Windows 10/11 SDK. Multi-gigabyte, needs
   administrator rights, so it is the owner's action rather than something to run unattended. Nothing in
   Stages 1–3 can start without it. **[recommendation as to which workload]**

   Confirm with this — all three must answer, and a bare `winget list` is not sufficient because VC++
   *redistributables* look superficially like a hit and contain no compiler:

   ```powershell
   & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -products * -property installationPath
   Test-Path "${env:ProgramFiles(x86)}\Windows Kits\10\Include"
   Test-Path "$env:ProgramData\Microsoft\VisualStudio\Packages"
   ```
4. ~~Spike TSF registration and hosting~~ — **done, from the registry, without a compiler.** All three
   questions answered plus a fourth found; see §14. **[fact]**
5. **Decide the two things the spike surfaced, before writing any C++:**
   **(a)** how TIP registration gets its administrator rights without turning the ordinary install into a
   UAC prompt — a separate opt-in step in Settings is the recommendation; **(b)** whether to ship a 32-bit
   TIP alongside the x64 one, or to declare 32-bit host applications unsupported. Both change the build and
   the installer, so they are cheaper to settle now than after the x64 DLL works. **[recommendation]**
6. **Then Stage 1 proper:** a registerable text service that receives context, with the existing path
   untouched and still primary.
7. **Build the compatibility matrix as you go, not at the end.** The brief requires per-application results
   and explicitly forbids claiming universal support. Record `works`/`partial`/`unsupported`/`fallback` per
   app in this file as each is tested.
8. **Fix the documentation debt** (§12 item 15) — `README.md` and `READ-ME-FIRST.txt` are two versions behind.
   Small, and it is the owner-facing documentation.
9. **Measure where the memory is going** (§12 item 9) — the baseline ~524 MB, and whether the settings card
   should quote a total rather than a delta now that the loaded figure is over a gigabyte.
10. **Make the flaky performance assertion take the best of three** (§12 item 10), so a red suite means
    something.
11. **Fix the autostart split-brain** (§12 item 13) and **resolve the inert `BackdropBlur` setting** (item 11).

**Release checklist, for whenever the next build ships:**

- Stop the running app first (it locks its own exe).
- Bump the version in **both** `installer/WordStrip.iss` and `src/WordStrip.App/WordStrip.App.csproj`.
- `dotnet test` (327), then `Verify-PersistentBar.ps1`, then `build-release.ps1`.
- Re-run `Verify-PersistentBar.ps1 -ExePath ...\publish\portable\WordStrip.exe` — the self-contained
  single-file build is a different code path (embedded dictionary) and starts more slowly.
- **Open every settings card** before shipping. §13 records why.
- Back up **the whole of** `%LOCALAPPDATA%\WordStrip` before touching the installer (§9). It now holds the
  personal vocabulary, learned data **and a 227 MB model**, and uninstall deletes the lot.
- Commit and tag.

## 16. Fresh-Chat Startup Prompt

```text
Read D:\Claude Code\WordStrip\CLAUDE_PROJECT_CONTEXT.md first, before doing anything else.

Then:

1. Confirm your actual current working directory. Note that the project root is
   D:\Claude Code\WordStrip — the working directory is usually its parent, D:\Claude Code,
   which is not a git repository. Use absolute paths.

2. Inspect the files listed in section 10 of that document before changing anything.
   Treat the context file as primary context, but verify it against the actual files —
   it can go stale.

3. Summarise your understanding of the project back to me in a few sentences.

4. State what you plan to do, based on section 14 (Current Task) and section 15
   (Recommended Next Steps). Propose a plan before acting.

5. Ask questions only if something is genuinely ambiguous or unsafe. Do not ask about
   anything already answered in the context file.

6. Continue from the documented current task: Phase 7, the TSF migration. The brief is at
   C:\Users\wordstrip-dev\Downloads\Worstripe\PHASE_7_Windows_TSF_Migration.md — read it.
   Its first rule is that the existing keyboard-hook input path must keep working; TSF is
   added alongside it, never in place of it. Section 14 lists three genuinely unresolved
   questions about TSF registration and hosting; establish those before building on
   assumptions.

7. Do not modify unrelated files. Respect the "load-bearing details" in section 13 —
   several of them look like harmless code but will silently break text insertion, the
   layout, or Tab and Esc system-wide if changed. The text injector in particular took
   four wrong fixes to get right; read its comments before touching it.

8. Whenever a major decision, feature or task status changes, update
   CLAUDE_PROJECT_CONTEXT.md so it stays accurate.

The project is under git (local only, branch master, no remote, so there is no off-machine
backup). Commit before substantial changes.
```
