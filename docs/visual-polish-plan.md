# Visual polish phase — audit and plan

The engine, insertion and interaction model are frozen for this phase. Nothing below changes what WordStrip
predicts, inserts, or how keys are routed. Everything below changes how it looks.

## 1. Audit (recorded before any change)

Measured from the running Release build: screenshots of the real bar over a real edit control at 100% scale,
and of the real Settings window, plus a read of every file in `src/WordStrip.App/UI`.

### Suggestion bar — what is already right

- Fixed slot geometry (`SlotPanel`): one size, one position for a whole sentence. Verified by
  `Measure-BarStability.ps1`.
- `GlassPlate` draws surface, rim, sheen and bezel directly, reports zero desired size, and is a static
  sibling of the animating lens so the shadow never re-renders during motion.
- `SelectionLens` is render-only and spring-animated; no layout runs when the selection moves.
- `GlassMetrics` derives every size from one scale factor, keeping radii concentric.
- `ThemeBrushes` is the single place tokens become pixels, with High Contrast and reduced-transparency
  fallbacks. Seven themes, one component tree.

### Suggestion bar — what this phase fixes

| Finding | Why it matters |
|---|---|
| Selection carries four signals at once: pill fill, pill rim, accent underline, text-colour flip, plus a weight change | Reads as a button inside a button. The spec asks for one primary signal and one quiet supporting one. |
| Passive and active look identical when Space is armed | No visual distinction between "informational" and "you are driving this". |
| `Divider = Solid(Text, 0.22)` | Dividers read as `looking | looked | looks`, competing with the words. |
| Chip weight depends on `IsSelected` | Selecting a word changes its weight and therefore its measured width. |
| Alternates sit at Medium/0.74 opacity, primary at Medium/1.0 | Weight hierarchy is carried almost entirely by opacity. |
| `BackgroundProbe` decides light-vs-dark only | A pale theme over a white page has no guaranteed separation. |

### Settings — findings

Stock WPF throughout: default ComboBox with its chevron and blue focus fill, default radio glyphs, default
scrollbar, a pinned "Done" footer overlaying scrolled content, all-caps section headers, one long
single-column scroll, multi-sentence explanatory paragraphs in the main visual path, and a title bar that
does not match either Windows or the bar.

## 2. Plan

Phases run in order; each ends with build, unit tests, real-typing regression, and a screenshot pass.

Status: all phases (A to H) are done.

- **A — Tokens.** One small token layer (`UI/Design/DesignTokens.cs` + `Tokens.xaml`): typography, geometry,
  motion, control metrics, focus. No new framework; it exists so B–G stop inventing numbers.
- **B — Bar.** Typography hierarchy by weight rather than opacity; weight independent of selection; quiet
  dividers; two selection states (armed = quiet pill, active = pill + indicator).
- **C — Placement, DPI, multi-monitor.** Done. Placement arithmetic moved to `Core/Presentation/BarPlacement.cs`
  (pure, unit-tested) and the window now positions itself with `SetWindowPos` in the target monitor's own
  physical pixels, chosen by `Interop/MonitorLayout.cs` from the caret. `SystemParameters.WorkArea` is gone
  from the bar: it is the primary display's rectangle at the primary display's scale, which is the wrong
  answer on any second monitor. Text rendering declared Grayscale after measuring that a layered window was
  never subpixel-rendering anyway. Verified at 150% on one display; 125/175/200% and a genuine mixed-DPI
  pair still need a machine with those displays.
- **D — Seven themes.** Done. `Core/Presentation/SurfaceSeparation.cs` enforces a floor of 0.12 on the
  *composited* luminance of the bar against the measured backdrop, raising opacity first and moving the
  surface's colour (hue kept) only if that is not enough; `BackgroundProbe`'s measurement is now carried into
  the palette rather than only deciding light-versus-dark. Measuring every variant against the backdrop it
  was authored for found nine of the fourteen below the floor — a pale theme over a white page was not a
  surface at all — so all seven themes were re-authored to clear it themselves, with sheen, bezel and shadow
  pulled back per theme. A test asserts the rescue path never runs for an authored theme.
- **E — Settings structure.** Done. A navigation rail with seven pages replaces one long scroll; the pinned
  "Done" footer is gone (settings were always applied live, so it only ever overlaid the content it sat on).
  `UI/Design/Controls.xaml` templates every control the window uses — toggle switches, segmented controls,
  sliders, buttons, text boxes, scrollbars, lists — against `Palette.Light.xaml` / `Palette.Dark.xaml`
  through DynamicResource, so the window follows the system's app mode, including its title bar. Discrete
  choices (words shown, light/dark, placement, motion) are segmented controls rather than sliders and radio
  lists. Keyboard shortcuts are shown as keycaps.
- **F — Theme gallery and sticky live preview.** Done. The Appearance page is split: settings scroll on the
  left, the preview holds still on the right. Seven tiles, each a real miniature strip drawn by the bar's own
  renderers over the backdrop that theme was authored for, with name, one-line description and a check on the
  chosen one. The preview shows four states at once — passive and Tab-selected, over a light page and a dark
  app — because the passive state is what the user actually looks at all day and the old preview never showed
  it. Everything updates as the theme, thickness, tint and word count change.
- **G — Personal vocabulary, learning/privacy, model status, integrations.** Done. A shared `FeatureState`
  (Off / Ready / Active / Attention) in the view model drives a status row said three ways — shape, word and
  colour, never colour alone. The model page reads "Active / Ready / Not installed" with the action beside
  it and the technical detail behind "Details"; Integrations leads with the state and then lists which
  applications it actually covers, which is the honest answer since the two input paths differ. The word
  list shows its count, reveals Remove on the row being pointed at (still keyboard-reachable), and says what
  to do when empty. Learning leads with a state and a number, with what-is-stored behind an expander.
- **H — Accessibility, reduced motion, regression and high-DPI polish.** Done. The bar's chip hover was the
  one animation that ran regardless of Windows' "animation effects" setting, because a XAML storyboard cannot
  ask; it is now a plain setter. `Palette.HighContrast.xaml` takes every colour from `SystemColors`, so under
  High Contrast the window keeps its layout and gives up its palette — matching what `ThemeBrushes` already
  did for the bar. `testsegression\Verify-Palettes.ps1` checks all three palettes parse and define the
  same keys, which is the only way to catch a missing key in a mode this machine cannot display. Keyboard
  focus verified by driving the whole window from the keyboard; layout checked at the minimum window size.

### Decisions taken during the audit

- **Shadow stays a single static `DropShadowEffect` on the plate**, tuned per theme in Phase D, rather than
  a two-pass or 9-slice implementation. It already sits on a static sibling that never re-renders while the
  lens animates, so it costs nothing during typing; a second pass would add cost to fix a problem the
  measurements do not show.
- **The lens keeps its spring.** Motion hierarchy is enforced by *when* it moves, not by how.
