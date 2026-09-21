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

- **A — Tokens.** One small token layer (`UI/Design/DesignTokens.cs` + `Tokens.xaml`): typography, geometry,
  motion, control metrics, focus. No new framework; it exists so B–G stop inventing numbers.
- **B — Bar.** Typography hierarchy by weight rather than opacity; weight independent of selection; quiet
  dividers; two selection states (armed = quiet pill, active = pill + indicator).
- **C — Placement, DPI, multi-monitor.** Caret-anchored placement with work-area clamping; per-monitor DPI
  checks at 100/125/150/175/200%.
- **D — Seven themes.** Tune each variant's tokens, including a minimum perceptual separation floor over
  white and near-black. No theme removed, no theme ranked.
- **E — Settings structure.** Navigation rail, section pages, live-apply, no footer.
- **F — Theme gallery and sticky live preview** on the Appearance page.
- **G — Personal vocabulary, learning/privacy, model status, integrations pages.**
- **H — Accessibility, reduced motion, regression and high-DPI polish.**

### Decisions taken during the audit

- **Shadow stays a single static `DropShadowEffect` on the plate**, tuned per theme in Phase D, rather than
  a two-pass or 9-slice implementation. It already sits on a static sibling that never re-renders while the
  lens animates, so it costs nothing during typing; a second pass would add cost to fix a problem the
  measurements do not show.
- **The lens keeps its spring.** Motion hierarchy is enforced by *when* it moves, not by how.
