# WordStrip

A phone-keyboard-style word suggestion strip for Windows. A thin glass bar floats above the taskbar
showing ranked word candidates as you type, plus offline autocorrect. Built because Windows' own
physical-keyboard suggestions don't work in most apps, and every third-party tool converged on single
inline ghost text rather than a candidate bar.

Everything runs locally. No network calls, no telemetry, no cloud model.

## Status

Working preview, 0.4.0. Verified two ways:

- **79 unit tests** over the prediction primitives and the suggestion controller.
- **An end-to-end regression** (`tests\regression\Verify-PersistentBar.ps1`) that drives a real Win32
  `Edit` control and reads the text back with `WM_GETTEXT` — not screenshots, which are meaningless here
  (see [Why screenshots can't verify this](#why-screenshots-cant-verify-this)). It covers live suggestions,
  Tab to highlight, Space to insert, Esc to dismiss, autocorrect on word commit, the bar persisting between
  words, and Tab still reaching the app while the bar is idle.

```bash
powershell -File "D:\Claude Code\WordStrip\tests\regression\Verify-PersistentBar.ps1"
```

It takes over the keyboard and foreground for about a minute. It types only into a throwaway window it
creates itself, and it re-checks that that window still has focus before every keystroke — otherwise a
stray focus change sends the test's typing into whatever you're actually doing.

## Requirements

- Windows 10 1809+ (Windows 11 recommended — the backdrop blur and rounded corners are Win11 APIs and
  simply no-op below that, leaving a solid panel)
- .NET 8 SDK to build. **Testers need nothing installed** — the shipped build is self-contained.

## Build and run

```bash
dotnet build "D:\Claude Code\WordStrip\WordStrip.sln"
```

```bash
"D:\Claude Code\WordStrip\src\WordStrip.App\bin\Debug\net8.0-windows\WordStrip.exe"
```

The app has no main window — it lives in the system tray. Startup takes a few seconds while the spelling
index builds in the background; the tray icon appears immediately. Launch with `--settings` to open the
settings window directly (this is what the Start Menu "WordStrip Settings" shortcut does). Only one copy
runs at a time — launching it again just opens Settings on the copy already running, because two keyboard
hooks would fight over every keystroke.

## Producing a shareable build

```bash
powershell -File "D:\Claude Code\WordStrip\build-release.ps1"
```

That produces, in `publish\`:

- `WordStrip-Setup-<version>.exe` — per-user installer. No UAC prompt, Start Menu entries, optional
  start-with-Windows, and a clean uninstall through Add/Remove Programs.
- `portable\WordStrip.exe` — one self-contained file, nothing beside it. The dictionary is embedded in
  the assembly precisely so copying just this file still works.

Both are unsigned, so Windows shows a SmartScreen "unknown publisher" warning on first run.
`publish\READ-ME-FIRST.txt` explains that to testers. The installer needs
[Inno Setup 6](https://jrsoftware.org/isdl.php); the script skips it with a warning if it isn't installed.

## Using it

Start typing in a supported text field. The bar appears above the taskbar with candidates.

| Key | Action |
|---|---|
| `Tab` | Highlight the next candidate (`Shift+Tab` for previous). Hold to scrub through them. |
| `Space` | Insert the highlighted candidate |
| `Enter` | Also inserts the highlighted candidate |
| `Esc` | Put the bar away |
| Click | Insert a candidate directly — no need to Tab first |

**Tab first, then Space.** Space only inserts when a candidate is actually highlighted — otherwise every
space you typed would rewrite the word you just finished. With nothing highlighted, Space is just a space.

### The bar stays put between words

By default the strip behaves like a phone keyboard's suggestion row: it stays on screen and shows common
words when you're between words, rather than vanishing after every space. The strip appearing and
disappearing on every word is the thing that reads as flicker when typing at speed.

It goes away when you click elsewhere, press `Esc`, or focus leaves a text field. Typing brings it back.
Turn it off with **Settings → Suggestions → Keep the bar on screen between words** to get the original
per-word behaviour.

**A visible bar is not an input mode.** The bar only intercepts `Tab`, `Space`, `Enter` and `Esc` while it
is offering completions for a word you are part-way through typing. Sitting there between words it claims
nothing: `Tab` still indents and moves between form fields, `Esc` still closes dialogs, `Space` is a space.
Between words, clicking is how you take a word.

That distinction only became load-bearing when the bar started persisting. Previously "the bar is visible"
and "a word is in progress" were the same condition, so routing on visibility was harmless. A persistent
bar is up almost continuously, and a router keyed on visibility would hold `Tab` and `Esc` hostage the
entire time you were in a text field. `SuggestionUpdate.IsIdle` is what keeps the two apart.

Autocorrect fires when you finish a word with space, Enter, or punctuation, and only when the typed word
isn't in the dictionary *and* a confident correction exists. It won't silently rewrite a low-confidence guess.

## Settings

Right-click the tray icon → Settings. Everything applies live and the window shows a real preview of the
strip drawn with the current values — there's no Apply button because there's nothing to apply. The preview
shows the bar over **both** a light and a dark page, because it floats over whatever app you're typing in
and a theme that only works over one of them isn't finished.

- **Theme** — seven visual personalities (see below)
- **Words shown** — 3 to 7, default 4
- **Keep the bar on screen between words** — on by default. Off restores the original behaviour, where the
  strip appears for the duration of each word and disappears the moment it's committed.
- **Autocorrect** — on/off
- **Material thickness** — 15% to 95%, default 62%. Thicker is more opaque and easier to read; thinner
  shows more of what's behind. This is the tradeoff Apple's material guidance describes, exposed directly.
- **Bar thickness** — 0.7× to 1.4×. Scales text, padding and radii together, so the proportions and the
  concentric corners hold at every size. A thin bar matters most when it follows the caret.
- **Animation speed** — 0.5× to 2.5×. Holding Tab always scrubs faster than the configured speed.
- **Bar position** — fixed at the bottom, following the text cursor, or fixed at the top.
  Cursor-following uses the caret rectangle the focused control reports and falls back to the bottom
  when an app doesn't report one.
- **Start with Windows** — registers under `HKCU\...\CurrentVersion\Run`

Settings persist to `%LOCALAPPDATA%\WordStrip\settings.json` and take effect on the next keystroke.
"Pause suggestions" in the tray menu is a temporary toggle and always starts off.

## Privacy and safety

- The suggestion bar and autocorrect are **disabled in password fields** (`ES_PASSWORD` style),
  checked on every keystroke.
- The keyboard hook keeps only the word currently being typed, in memory. Nothing is written to disk.
- The bar never takes keyboard focus (`WS_EX_NOACTIVATE`), so it can't steal input from the app you're in.
- No network access of any kind. The prediction engine and dictionary are entirely local.

## Themes

Seven visual personalities of the same product. They are **presets, not implementations**: one component,
one geometry system, one interaction model and one motion system, with only the material tokens differing.
Everything a theme can change lives in `ThemeCatalog`; nothing else in the app knows a theme exists.

| Theme | Character |
|---|---|
| Fluent Acrylic | Translucent Windows 11 utility surface. The native-feeling default. |
| Mica-inspired | Calmer and more opaque, blur pulled right back. |
| Fluent 2 + Depth | Deeper and more dimensional, with a clearly elevated selection. |
| Apple Frosted | Bright, minimal, typography-led. Restraint rather than optical simulation. |
| Raycast Floating | Dark, dense, high contrast. The words do the talking. |
| visionOS-inspired | Pale and spatial, with a generous shadow so it floats above the app. |
| Material 3 | Tonal surface with a tinted selection container. |

Each theme is authored **twice** — once for bright backdrops, once for dark — and `BackgroundProbe` picks
between them from the actual screen luminance behind the bar. Dark variants are written by hand, never
derived by inverting the light ones, because inverted colours are how themes end up muddy.

### Why there is no backdrop-blur setting

The bar renders with WPF per-pixel alpha (`AllowsTransparency`). That is what makes each theme's authored
colour and opacity come out exactly as designed, and it gives true rounded corners.

It also rules out the DWM Mica/Acrylic backdrops, which cannot be applied to a layered window — so themes
supply their own translucency instead of a system blur. Content behind the bar still shows through; it just
isn't blurred. That trade is deliberate and follows the brief's own priority order, which ranks legibility
and responsiveness above material realism.

It is worth knowing *why* this mattered, because the symptom was baffling: with `AllowsTransparency="False"`
every theme rendered as the same washed-out grey. Painting an opaque red surface came out as `#FF5454` —
pure red at roughly two-thirds opacity, blended with white. The themes were correct all along; the window
was compositing them incorrectly. Per-pixel alpha renders that same red as exactly `#FF0000`.

## Design notes

The strip follows Apple's Liquid Glass guidance rather than approximating it by eye:

- It is a **floating functional layer above content**, which is the case that material is meant for, and
  it's the only place in the app that uses it — the guidance is explicit that overusing glass distracts
  from the content it's supposed to defer to.
- It uses the **regular** rather than clear variant, because the bar is nothing but text and the regular
  variant is the one that adjusts background luminosity to protect legibility.
- Corners use **continuous curvature**, not circular arcs — see `SquircleGeometry`. A circular corner jumps
  from zero curvature on the straight edge to constant curvature the instant the arc starts, and that
  discontinuity is what makes a corner look slightly pinched. Each corner is one cubic Bézier fitted to a
  fourth-order superellipse (control offset 0.909r, against 0.5523r for a true circle).
- Corner radii are **concentric**: the chip radius plus the inset and rim *is* the plate radius, derived in
  `GlassMetrics` rather than hard-coded, so the relationship survives any bar thickness.
- The material is layered the way real glass behaves — a tinted scrim, a specular sheen along the lit top
  edge, a rim that's bright at the top and falls away toward the bottom, and a **lensing band** just inside
  that rim. Real glass bends light at its edge, so the perimeter reads brighter where light enters and
  darker opposite; without it the surface stays flat no matter how good the blur behind it is.
- The tint is **not one fixed colour**. `BackgroundProbe` samples screen luminance just outside the bar and
  switches between smoked glass (dark scrim, for bright documents) and frosted glass (pale scrim, for dark
  ones). A permanently dark scrim works over a white page and then disappears over a dark one, where it has
  nothing to separate itself from. Sampling happens outside the bar's own bounds — reading where the bar
  already is would feed its own tint back in — with hysteresis so a mid-grey backdrop can't flip it
  back and forth mid-sentence.
- Motion is **spring-based**, not bezier — see `SpringEase`, which implements SwiftUI's
  `spring(response:dampingFraction:)`. The selection is a single lens that glides and stretches between
  chips rather than a per-chip highlight blinking on and off. Animations omit a `From` value so
  re-triggering mid-flight continues from wherever the property is, which is what makes rapid Tab presses
  feel continuous instead of jerky.

### Frame budget

Smoothness was treated as a correctness problem, measured rather than eyeballed — set `WORDSTRIP_FRAMELOG=1`
and `FrameProbe` writes per-frame intervals to `%TEMP%\wordstrip_frames.log`. The first measured build held a
17 ms median but dropped one to two frames per Tab. Three things were responsible:

- **The drop shadow sat on the same element as the moving lens.** A `DropShadowEffect` pushes its whole
  subtree through an offscreen pass whenever anything inside changes, so the blur re-ran every frame. The
  plate is now a separate static sibling, which is enough on its own: nothing inside it animates, so the
  blur no longer re-runs. It is deliberately **not** `BitmapCache`d — `GlassPlate` reports zero desired
  size (see [the layout trap below](#a-layout-trap-worth-knowing-about)), and a cache sized from a
  zero-size element caches nothing, so the plate simply stops being drawn.
- **The lens animated `Width` and `Canvas.Left`,** both of which invalidate layout, so WPF ran measure and
  arrange on every frame. `SelectionLens` is a custom element whose position and size are `AffectsRender`
  dependency properties, so animating them re-runs `OnRender` and nothing else.
- **The window was repositioned on every keystroke.** Moving a top-level window is a compositor operation;
  it is now skipped unless the target position actually changed.

### A layout trap worth knowing about

The plate and sheen were briefly `Path` elements. A `Path` reports its geometry's bounds as its desired
size, and that geometry was generated *from* the bar's measured size — so once the bar grew wide for a long
candidate list, the stale geometry kept demanding that width and the window could never shrink again. Short
words afterwards left a stretch of empty glass on the right. `GlassPlate` and `SelectionLens` both report
zero desired size and paint into whatever area they're given, so the chips alone decide how wide the bar is.

Both were also verified by measurement rather than inspection: `test-width` types a long-candidate prefix
and then a short one, and asserts the gap between window width and chip width stays constant (27 px, the
insets) instead of growing.

Durations were also cut — a suggestion strip is read mid-sentence, so motion only has to show *which* chip
moved. Holding Tab is detected as a scrub and switches to a shorter critically-damped spring, because a
spring tuned for a single press never reaches its target between 30-per-second repeats and the lens ends up
lagging further behind on every tick.

### Accessibility

Per Apple's guidance that translucency and motion must adapt to people's needs, the bar reads the Windows
equivalents of those settings (`SystemAppearance`) and degrades honestly rather than ignoring them:

- **Transparency effects off** or a **High Contrast theme** → the glass, sheen and shadow are dropped for a
  flat opaque panel with a solid rim. The settings window says so explicitly, so the sliders don't look broken.
- **Animation effects off** → the bar and the selection lens appear and move instantly instead of springing.
- Label text is 15px semibold white on a dark scrim, and the selected chip flips to near-black on the light
  lens — white-on-white at the moment of selection would otherwise be the one unreadable state.
- Chips are 30px tall with 12px between them, above the 28px minimum control size.

## Testing

Prediction primitives and the suggestion controller are unit-tested (`tests\WordStrip.Core.Tests`, 79
tests). Everything that depends on a system-wide hook, live focus inspection or `SendInput` reaching
another process is covered by the end-to-end regression instead.

```bash
dotnet test "D:\Claude Code\WordStrip\tests\WordStrip.Core.Tests\WordStrip.Core.Tests.csproj"
```

`IFocusedControlProvider` exists for the same reason `ITextInjector` does. The focus check read live Win32
state through a static call, so under a test runner it always reported "not a text field" and nothing that
depended on it could be tested at all.

### Why screenshots can't verify this

`PrintWindow` captures only what an app draws itself, never the DWM backdrop behind it, so judging the
glass from a `PrintWindow` grab is meaningless. PowerShell is also DPI-unaware by default — call
`SetProcessDPIAware()` first or captures land in undersized bitmaps and are silently cropped (this display
runs at 150%). Read text back with `WM_GETTEXT` instead.

### Three things that make the regression harness lie

Each of these produced a convincing false failure before being fixed:

- **A WinForms `TextBox` is not a supported target.** Its class is `WindowsForms10.EDIT.app.0.<hash>`,
  which does not start with `Edit`, so `FocusedControlInspector` correctly ignores it and no bar ever
  appears. The harness creates a real `EDIT` control with `CreateWindowEx` instead. Notepad is no good
  either: Windows 11 ships it as a packaged single-instance app, so `notepad.exe` exits immediately and
  returns no window handle.
- **`SendKeys` types faster than any keyboard.** It delivers a whole string in microseconds, and text
  replacements are deliberately deferred onto the message loop, so the burst is still draining into the
  target when the replacement fires and the two interleave — turning `helo ` into `healo`. That is the
  harness outrunning a real keyboard, not a defect. It now sends one key at a time.
- **Pick a misspelling with only one plausible correction.** `helo` is one edit from `help` *and* from
  `hello`, so the tie falls to frequency and `help` wins — correct behaviour, terrible assertion. `teh`
  is one transposition from `the`, which then outweighs every neighbour by orders of magnitude.

`SetForegroundWindow` is also silently refused from a background process; the harness uses the
`AttachThreadInput` technique, and verifies the focused window before every keystroke.

## Current limitations

**App coverage is the main one.** v1 detects standard Win32 text controls (`Edit`, `RichEdit`), which
covers Notepad and most desktop app text boxes and dialogs. It does **not** yet work in:

- Chromium-based apps (Chrome, Edge, Electron) — they report `Chrome_WidgetWin_1` and render text themselves
- Modern XAML/WinUI surfaces that don't expose a classic edit control
- Office's own text surfaces

Other known gaps:

- English only (single bundled dictionary)
- Suggestions are dictionary frequency + edit distance — no context awareness, no phrase prediction,
  and no learning from your own writing
- The word buffer is a best-effort shadow of the caret. Arrow keys, clicks, and Ctrl/Alt combos reset it
  rather than risk drifting out of sync with the real text

## Architecture

```
WordStrip.Core/          no UI dependencies
  Input/                 WH_KEYBOARD_LL hook, SendInput injection, key→char translation, word buffer
  Prediction/            prefix index, SymSpell-style delete index + Damerau-Levenshtein, ranking
  Automation/            focused-control + password-field detection, behind IFocusedControlProvider
  Suggestions/           SuggestionController — the only class the UI talks to
  Settings/, Platform/   JSON settings store, autostart registration

WordStrip.App/           WPF, net8.0-windows
  UI/                    glass suggestion bar, settings window
  Interop/               DWM backdrop, no-activate window styles
  Coordination/          BarInputRouter — Tab/Enter/Esc handling
  Tray/                  NotifyIcon and context menu

tests/
  WordStrip.Core.Tests/  xUnit, 79 tests
  regression/            end-to-end scripts driving a real Win32 edit control
```

Two design decisions worth knowing before changing anything:

**Text injection sits behind `ITextInjector`.** The current implementation is SendInput
(backspace the typed word, retype the replacement). The intended upgrade path is a Text Services
Framework text service — the same machinery IMEs use for candidate bars — which would fix the
Chromium gap properly. Swapping it shouldn't require touching the UI or prediction code.

**Hook subscription order is load-bearing — on both hooks.** `BarInputRouter` must subscribe to the
keyboard hook *before* `TypingSession.Attach()` is called. Handlers run in subscription order, and the
contract is that anything the router suppresses, `TypingSession` skips. Reverse it and Tab resets the word
buffer and tears the bar down mid-cycle. This is why `TypingSession` separates construction from `Attach()`.

The same applies to the mouse hook. A click outside the bar dismisses it, and `TypingSession` reacts to
that same click by resetting its buffer — which, with a persistent bar, republishes the between-words list.
Dismissing first means that republish is suppressed. Dismissing second makes the bar flash back on for a
frame on every outside click.

**Dismissal is sticky, and that is the point.** `SuggestionController.Dismiss()` sets a flag that survives
until the user types again, rather than just publishing an empty list. Once the bar repopulates itself
between words, an ordinary hide no longer sticks — the next buffer reset would put it straight back. This
is also why `Esc` routes through the controller instead of calling `HideBar()` on the window.

**Injected-key detection uses a private marker, not `LLKHF_INJECTED`.** That flag is set for SendInput
from *any* process, so relying on it would make the app ignore dictation software, automation tools, and
remote input. Our own injected keys carry a sentinel in `dwExtraInfo` instead.

**Text replacement never runs inside the hook callback.** `SuggestionController` takes a
`postToMessageLoop` delegate and routes every injection through it. Calling `SendInput` from within the
low-level keyboard hook fails two different ways: if the triggering key is suppressed the injected input
is discarded outright, and if it isn't, the injection interleaves with the key still in flight and
scrambles the text (`wor` + space once produced `f or `). Deferring to the dispatcher fixes both.

**Replacements edit as little as possible.** The injector keeps the shared prefix between what you typed
and the chosen word, so completing "wor" → "world" sends no backspaces at all — it just types "ld". It also
re-applies your capitalisation ("Wor" → "World", not "world"). Deleting and retyping a whole word makes some
rich-text controls re-derive character formatting for the reinserted run, so touching less text is both
faster and less likely to disturb the surrounding style.

**`INPUT` must include the `MOUSEINPUT` union member.** It's never used, but it sets `sizeof(INPUT)` to
the 40 bytes x64 expects. With only `KEYBDINPUT` declared the struct is 32 bytes, and `SendInput` rejects
every call with `ERROR_INVALID_PARAMETER` while returning a count nobody checks — so injection silently
does nothing. `Win32TextInjector.Send` now throws when fewer events are inserted than requested.

## Credits

Word frequencies from [SymSpell](https://github.com/wolfgarbe/SymSpell) (MIT),
`frequency_dictionary_en_82_765.txt`, top 60,000 entries loaded.
