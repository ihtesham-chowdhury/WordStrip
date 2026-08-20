# WordStrip

A phone-keyboard-style word suggestion strip for Windows. A thin glass bar floats above the taskbar
showing ranked word candidates as you type, plus offline autocorrect. Built because Windows' own
physical-keyboard suggestions don't work in most apps, and every third-party tool converged on single
inline ghost text rather than a candidate bar.

Works in **Chrome, Edge, Brave, Electron apps and Microsoft Word** as well as Notepad and classic Win32
dialogs, via an optional Windows Text Services Framework text service.

Everything runs locally. No network calls, no telemetry, no cloud model.

## Status

Working preview, 0.11.1. Verified two ways:

- **364 unit tests** over the prediction primitives, the language model, phrase generation, emoji matching,
  personal vocabulary and learning, text injection, the suggestion controller and the typing-history rules.
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

**The bar owns Tab whenever it is showing anything** — completing a word or predicting the next one. That
was not always so: the between-words bar briefly claimed no keys at all, to keep Tab indenting and moving
between dialog fields. In use that was the wrong trade, because it put the predictions out of reach on
exactly the path where they are most useful, straight after inserting a word. Esc is the escape hatch:
dismiss the bar and Tab behaves normally again until the next keystroke brings it back. Esc itself is only
swallowed when there is a highlighted candidate to cancel, so it still closes a dialog otherwise.

### The bar stays put between words

By default the strip behaves like a phone keyboard's suggestion row: it stays on screen and predicts what
comes next when you're between words, rather than vanishing after every space. The strip appearing and
disappearing on every word is the thing that reads as flicker when typing at speed.

Type `how are` and it offers *you, we, they*. Type `let me` and it offers *see, go, have, know*. See
[Context-aware prediction](#context-aware-prediction) for where that comes from.

It goes away when you click elsewhere, press `Esc`, or focus leaves a text field. Typing brings it back.
Turn it off with **Settings → Suggestions → Keep the bar on screen between words** to get the original
per-word behaviour.

Tab cycles these exactly as it cycles completions — see [Using it](#using-it) for why that ended up being
the right call, and what Esc does about it.

Autocorrect fires when you finish a word with space, Enter, or punctuation, and only when the typed word
isn't in the dictionary *and* a confident correction exists. It won't silently rewrite a low-confidence guess.

## Context-aware prediction

Suggestions are conditioned on the words before the cursor, not just on the letters typed so far. Two
questions, two modes:

| You are | The question | What answers it |
|---|---|---|
| `wor⎸` | which words start like this? | prefix + fuzzy matching, reordered by context |
| `I am looking ⎸` | what usually comes next? | the n-gram model |

The two are kept apart deliberately. While there is a partial word the user has told us something concrete
and completion leads; context only reorders the candidates it produces, never adds to them, because
offering a word that doesn't match the letters on screen is worse than offering nothing. Once the word is
finished there is nothing to complete and the model leads.

### The model

Trigrams and bigrams, built offline into two tab-separated text files (`assets/ngram/`) holding conditional
log probabilities. 120k bigram and 227k trigram entries over the same 60,000-word vocabulary the completion
engine uses — the model can only ever suggest a word the rest of the app knows.

Text rather than a binary blob so it stays diffable and hand-editable, and so a regenerated model shows up
in a diff as a real change. It costs load time against a packed layout, but loading happens once on the
background thread that already builds the spelling index.

### Probabilities, not counts

Two sources, and their raw counts are not comparable: SymSpell's bigram counts come from Google Books and
run to the billions, while counts from a few dozen novels run to the thousands. Summing them would let one
source erase the other. Conditional probabilities mix directly, so each source is reduced to a distribution
first and the blend is a genuine mixture. Where only one source knows a context, that source is the whole
distribution rather than half of it — mixing against an implicit zero would penalise contexts the other
source simply never saw, which is not evidence against them.

### Backoff

Stupid backoff (Brants et al., 2007): try the trigram, fall back to the bigram, then to plain word
frequency, multiplying by a fixed penalty at each step down.

```
S(w | w₁w₂) = P(w | w₁w₂)          if the trigram is known
            = 0.4 · S(w | w₂)       otherwise
S(w | w₂)   = P(w | w₂)             if the bigram is known
            = 0.4 · S(w)            otherwise
```

It is a score, not a normalised distribution, and does not pretend otherwise. Proper discounting
(Kneser-Ney and relatives) buys accuracy that matters when measuring perplexity over a corpus and buys
nothing when ordering seven words on a strip.

A trigram hit doesn't end the search. If a trigram context knows only three continuations and the bar has
room for seven, the rest come from the bigram tier and then from raw frequency — each penalised so it can
never displace better-evidenced words above it.

### How context is scored

`ContextualRanker` wraps `FrequencyRanker` rather than replacing it. The base score is Phase 1's, unchanged,
so exact matches still beat prefix completions and prefix completions still beat fuzzy ones no matter what
the model thinks. That ordering is about what the user is demonstrably typing; context is only ever an
opinion about what they might mean.

**The bonus is capped below the gap between bands.** Context reorders candidates within a band; it cannot
lift one out of it. Without that cap a confidently predicted word could outrank one whose letters are
already on screen, and the bar would start fighting the typist.

Within a band, probability is weighted well above frequency — because a conditional probability has
*already* accounted for how common a word is, and letting raw frequency speak again double-counts it. That
was not a theoretical concern: at a low weight, "I am" suggested *the* and *to* — real continuations, and
useless ones — while burying *sure* and *going*.

### Where the context comes from

`TypingSession` keeps the last two finished words, held to exactly the same standard as the in-progress word
buffer: it is a shadow of text the app cannot read, so it is dropped rather than guessed at the moment
anything could have moved the caret — a click, an arrow key, a Ctrl combo, a backspace into untracked text.

**Stale context does not degrade gracefully.** It produces confident, specific suggestions conditioned on
words that are no longer behind the cursor, which is indistinguishable from the model being broken. Dropping
it is always the better failure.

A full stop clears the history too, and substitutes a sentence-start marker, so the model answers "what
opens a sentence?" instead of carrying the previous sentence's last word across the boundary. The marker is
a valid context but never a suggestion — it was briefly the single most probable continuation of "thank
you", which would have shown the user a blank chip.

Autocorrect rewrites the last history entry to the corrected word, and accepting a suggestion adds it to the
history rather than clearing it. Both matter: the model must predict from what is on screen, not from what
was typed.

### Regenerating the model

```bash
powershell -File "D:\Claude Code\WordStrip\tools\ngram\Fetch-Corpus.ps1"
```

```bash
dotnet run --project "D:\Claude Code\WordStrip\tools\WordStrip.NGramBuilder" -c Release
```

The corpus lands in `.corpus\` (gitignored, ~47MB); only the generated model is committed. Pruning is
tunable — `--min-bigram`, `--min-trigram`, `--top` — and every setting is recorded in the output file's
header. Output is deterministically sorted, so rebuilding from the same corpus produces a byte-identical
file.

## Phrases and emoji

### Multi-word suggestions

Between words the bar can offer several words as one candidate — "forward to" rather than just "forward" —
generated by bounded beam search over the same n-gram model, never from a hard-coded phrase list.

```
context ──► top next words ──► extend each ──► score ──► keep the best few ──► repeat, up to 3 words
```

Three rules do most of the work, and all three exist to stop the feature being annoying rather than to make
it impressive:

**Longer must earn it.** A phrase is scored on its *mean* log probability per word, not its total. Tacking
on a vague word actively lowers the score, so a confident one-word prediction beats a padded three-word one.
Without this, "for" quietly becomes "for the" for no reason but length.

**Extensions need three-word evidence.** A continuation is only accepted if the corpus has seen that exact
three-word sequence. Allowing a backoff would mean extending on "what usually follows this one word", which
is how a phrase generator produces fluent nonsense — after "how are we", "to" commonly follows "we" and
"do" commonly follows "to", and you end up offering "we to do" with no idea whether it parses.

**Uncertainty shortens, never lengthens.** If the seed word came from the unigram fallback, the model has
never seen this context and is offering common English; building a phrase on that produces a plausible
sentence fragment unrelated to what the user is writing. Those seeds stay single words.

Deduplication keeps only the longest form of a given opening, so the bar shows a spread — "forward to",
"at", "about" — instead of "forward", "forward to" and "forward to the" in three slots.

Measured at **~350 µs per keystroke** against the shipped model, well inside the per-keystroke budget.

### Emoji

Type "pizza" and 🍕 is offered at the end of the bar, the way a phone keyboard does. A curated table of a
few hundred keywords, bundled and offline.

The rules are deliberately narrow, because a wrong emoji is more jarring than a wrong word — it is the only
thing on the strip the eye goes to:

- **At most one, always last.** It takes the weakest slot rather than being appended, so the bar keeps the
  width you configured, and it never displaces more than one word.
- **Only on an unambiguous match.** An exact keyword, or a prefix that exactly one emoji fits. "piz" gives
  🍕; "cal" gives nothing, because it opens both "calendar" and "call".
- **Placed by policy, not by score.** An emoji has no frequency and no context probability, so any score it
  were given would be invented. A rule that can be stated in a sentence is easier to reason about — and to
  test — than a number tuned until the output looked right.

Both are switchable in **Settings → Suggestions**.

## Your own words, and learning

Two separate features, deliberately: one you fill in, one that fills itself in. Both are local, both are
plain files you can read and delete, and neither has any network code beneath it.

### Personal vocabulary — words you add

Names, products, jargon, project codenames: anything the 60,000-word dictionary has never heard of. Added
through **Settings → Your words**, or imported from a word-per-line text file.

They do two things:

- **They become suggestable.** Type `qn` and `QNAP` is offered, even though no general dictionary contains it.
- **They are protected from autocorrect.** An unknown word a few edits from a real one is exactly what
  autocorrect exists to fix, so without this, mentioning your NAS would rename it. Adding a word is the
  user saying it is spelled correctly, and `IsCorrectlySpelled` consults the personal list first.

**Casing is preserved separately from the lookup form.** `QNAP`, `GitHub` and `iPhone` are the sort of word
a personal vocabulary is *for*, and half of them are words the dictionary gets wrong about capitalisation
rather than spelling. Entries store a normalized key and a display form, so matching is case-insensitive
while insertion is not. The casing first chosen wins over whatever gets typed later — deliberate beats
incidental.

Personal words are competitive without being automatic. Carrying no corpus frequency, they would otherwise
sink below every common word sharing their prefix; a bounded bonus clears that gap and puts them just above.
It cannot lift them out of their band, so a word you have actually finished typing still wins.

### Personal learning — what it picks up

**Off by default.** Everything else in Settings changes how the app looks or behaves; this changes what it
records about the person using it, and that is not a reasonable thing to switch on for someone without
asking.

Switched on, it counts words, pairs and triples as you finish them, and leans suggestions toward how you
write. Type "Northfield Data Systems" enough times and `British` starts predicting `Council`.

What it does **not** do:

- No sentences, documents or keystroke log. The file holds counts against sequences of at most three words
  — enough to know what tends to follow what, not enough to reconstruct anything you wrote.
- **Never learns from a password field**, or from any control the app could not positively identify as an
  ordinary text box. The learning call sits behind the same focus check that decides whether to suggest at
  all, so "we could not tell what this field is" always means "do not learn from it".
- No network, no account, no telemetry, no sync.

**It forgets.** Counts saturate at 1,000 and the whole model decays by 10% every 20,000 words, so a phrase
hammered last year fades instead of owning the bar forever. Tables are capped at 20,000 entries per order
and pruned by usage when full. Measured growth: **394 KB after 100,000 words**, and flat thereafter.

**It arrives gradually.** A personal model built from a few sentences is not evidence, so its influence
ramps linearly to full weight at 2,000 learned words. Without that, switching the feature on would visibly
change behaviour a paragraph later and then change it again — the cold-start problem.

**Settings → Learning** shows exactly how much has been learned and offers a single button to delete all of
it. Clearing removes the file rather than blanking it: "delete my data" should not leave a tidy record that
there used to be data.

### Where it all lives

| File | What it holds |
|---|---|
| `%LOCALAPPDATA%\WordStrip\settings.json` | Preferences |
| `%LOCALAPPDATA%\WordStrip\personal-vocabulary.json` | Words you added, with casing and usage counts |
| `%LOCALAPPDATA%\WordStrip\personal-language-model.json` | Learned counts, only if learning is on |

Both stores write via a temporary file and then replace the original, so an interrupted write cannot leave a
truncated file behind — the learning model saves on a timer, which makes that a question of when rather
than if. A corrupt file loads as empty rather than failing startup, and is left on disk for recovery instead
of being overwritten.

⚠️ **Uninstalling deletes all three.** The installer removes `%LOCALAPPDATA%\WordStrip` entirely. Export
your word list first if you want to keep it.

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
- **Suggest whole phrases** — on/off
- **Suggest emoji** — on/off
- **Animation speed** — 0.5× to 2.5×, and **fully right switches animation off entirely**. Off is a
  different thing from fast, and the end of the travel is where someone already drags to ask for it.
  Holding Tab always scrubs faster than the configured speed.
- **Bar position** — fixed at the bottom, following the text cursor, or fixed at the top.
  Cursor-following uses the caret rectangle the focused control reports and falls back to the bottom
  when an app doesn't report one.
- **Your words** — add, remove, import and export your personal vocabulary
- **Learning** — off by default; shows what has been learned and clears it
- **Start with Windows** — registers under `HKCU\...\CurrentVersion\Run`

Settings persist to `%LOCALAPPDATA%\WordStrip\settings.json` and take effect on the next keystroke.
"Pause suggestions" in the tray menu is a temporary toggle and always starts off.

## Privacy and safety

- The suggestion bar, autocorrect **and personal learning** are **disabled in password fields**
  (`ES_PASSWORD` style), checked on every keystroke. Learning sits behind the same check that gates
  suggesting, so a field the app cannot identify is never learned from.
- The keyboard hook keeps only the word currently being typed plus the two before it, in memory. Nothing is
  written to disk unless you switch learning on, and then only counts — never text.
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

Prediction primitives, the language model, phrase generation, emoji matching, the personal stores and the
suggestion controller are unit-tested (`tests\WordStrip.Core.Tests`, 364 tests). Everything that depends on
a system-wide hook, live focus inspection or `SendInput` reaching another process is covered by the
end-to-end regression instead.

The regression can drive either a plain `EDIT` control or a `RICHEDIT50W`, at a configurable typing rate:

```bash
powershell -File "D:\Claude Code\WordStrip\tests\regression\Verify-PersistentBar.ps1" -ControlClass RichEdit -PerKeyMs 25
```

A plain `EDIT` processes input too synchronously to expose ordering races at all, which is worth knowing
before concluding that one is fixed. `WORDSTRIP_DATA_DIR` points the app at a throwaway data folder so the
run can seed a known personal vocabulary without touching the real one.

The personal-store tests write real files to a temporary directory rather than using an in-memory fake.
Persistence is most of what those classes do, and the failures worth catching — a corrupt file, an
interrupted save, a hand-edited entry — only exist on disk.

The language-model tests run against a hand-written fixture model, not the shipped one. Asserting on the
real model would be asserting on what a few dozen novels happen to contain, and every assertion would break
the next time the corpus changed.

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

**App coverage** used to be the main limitation and is now largely solved. Two input paths run side
by side: a low-level keyboard hook for classic Win32 controls (`Edit`, `RichEdit`), and an optional
Windows Text Services Framework text service that reads the document directly.

With the text service registered, prediction has been confirmed working by hand in **Chrome, Brave, Edge,
Electron apps and Microsoft Word**, as well as Notepad and Win32 dialogs. Password fields are correctly
suppressed. Without it, the keyboard hook still covers classic controls, so nothing depends on it.

What still does not work through the text service:

- **Autocorrect and personal learning in browsers.** Committing text back through TSF is not implemented,
  so corrections stay on the keyboard-hook path. Prediction works everywhere; corrections do not.
- **32-bit applications** never load the text service (it ships x64 only) and fall back to the hook.

Other known gaps:

- English only (single bundled dictionary and corpus)
- **Tab is claimed while the bar is visible.** That is deliberate and was chosen over the alternative, but
  it does mean Tab will not indent or move between fields until you press Esc. If you spend more time
  tabbing between fields than taking suggestions, turn off "keep the bar on screen between words".
- **The corpus skews literary.** It is mostly 19th- and early-20th-century novels, so it is good at ordinary
  English sentences and weak on modern, technical or workplace phrasing. It has never seen "pull request" —
  though personal learning will pick that up from you if you switch it on.
- Predictions are statistical, not semantic: three words of context and no understanding. Phrases inherit
  the corpus's register, so they can read distinctly Victorian — "thank you" is as likely to suggest
  "sir said the" as anything you would write in an email. Personal learning is the practical answer.
- **Autocorrect cannot correct *into* a personal word.** Personal words are protected from being corrected
  away and are offered as completions, but the fuzzy index is built from the general dictionary at startup,
  so typing "githb" will not produce "GitHub".
- The word buffer and its history are a best-effort shadow of the caret. Arrow keys, clicks, and Ctrl/Alt
  combos reset both rather than risk drifting out of sync with the real text

## Architecture

```
WordStrip.Core/          no UI dependencies
  Input/                 WH_KEYBOARD_LL hook, SendInput injection, key→char translation, word buffer
  Prediction/            prefix index, SymSpell-style delete index + Damerau-Levenshtein, ranking
    NGram/               trigram/bigram model, shared tokenizer, on-disk format
  Personal/              the user's own vocabulary and learned counts — local files, no network
  Automation/            focused-control + password-field detection, behind IFocusedControlProvider
  Suggestions/           SuggestionController — the only class the UI talks to
  Settings/, Platform/   JSON settings store, autostart registration

WordStrip.App/           WPF, net8.0-windows
  UI/                    glass suggestion bar, settings window
  Interop/               DWM backdrop, no-activate window styles
  Coordination/          BarInputRouter — Tab/Enter/Esc handling
  Tray/                  NotifyIcon and context menu

tests/
  WordStrip.Core.Tests/  xUnit, 364 tests
  regression/            end-to-end scripts driving a real Win32 Edit or RichEdit control

tools/                   build-time only, never shipped
  ngram/                 corpus fetcher
  WordStrip.NGramBuilder/  turns the corpus into the model files
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

**The deletions and the replacement text go in one `SendInput` call.** Windows guarantees that events
inside a single call reach the target serially without other input interleaving, and promises nothing
between two calls. Sending them separately lets the target begin processing the backspaces while the text is
already arriving, and the still-draining deletions eat the front of it.

That bug shipped, and it hid for a long time because the common case has nothing to delete — a shared
prefix means zero backspaces and only one call. It took a personal-vocabulary entry whose capitalisation
differs from what was typed, which shares no prefix at all, to expose it: "Alexandra Fairbourne Reed"
accepted after typing "ale" arrived as "exandra Fairbourne Reed".

**`INPUT` must include the `MOUSEINPUT` union member.** It's never used, but it sets `sizeof(INPUT)` to
the 40 bytes x64 expects. With only `KEYBDINPUT` declared the struct is 32 bytes, and `SendInput` rejects
every call with `ERROR_INVALID_PARAMETER` while returning a count nobody checks — so injection silently
does nothing. `Win32TextInjector.Send` now throws when fewer events are inserted than requested.

## Credits

Word frequencies from [SymSpell](https://github.com/wolfgarbe/SymSpell) (MIT),
`frequency_dictionary_en_82_765.txt`, top 60,000 entries loaded.

Bigram frequencies from the same project, `frequency_bigramdictionary_en_243_342.txt` (MIT).

Trigrams, and a second opinion on bigrams, derived from public-domain texts from
[Project Gutenberg](https://www.gutenberg.org/). Only the derived counts are redistributed here; the book
IDs used are listed in `tools/ngram/Fetch-Corpus.ps1` and the texts themselves are not committed.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Read the licensing section at the top of it first — contributions
carry a relicensing grant, and it is better to know that before writing code than after.

Security issues should be reported privately: see [SECURITY.md](SECURITY.md). This project registers a
component that Windows loads into every application you type in, so that document is worth reading before
you install it, not just before you report something.

## Licence

Apache License 2.0. See [LICENSE](LICENSE) for the full terms and [NOTICE](NOTICE) for attribution.

You may use, modify and redistribute WordStrip, including commercially, under those terms. Apache 2.0 also
carries an explicit patent grant, which matters more than usual in text input and prediction.

Third-party components — the SymSpell dictionary, ONNX Runtime, the optional DistilGPT2 model — are listed
with their own licences in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

A commercial licence can be negotiated separately if Apache 2.0 does not suit your situation. That is an
additional option and takes nothing away from the open source grant — see [NOTICE](NOTICE).
