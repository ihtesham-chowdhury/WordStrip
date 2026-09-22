# Six themes, and a bar that sizes itself

The phase after the visual polish pass. The engine, insertion and interaction model were locked for it, as
they were for the previous one: nothing here changes what WordStrip predicts or how keys are routed.

Two problems were being solved.

1. **Seven themes were not seven themes.** Acrylic and Mica were one design at two opacities; Apple Frosted
   and visionOS were each other with the contrast moved; Fluent Depth and Raycast likewise. Rendered in grey,
   several pairs were indistinguishable.
2. **The bar was one size regardless of the text.** Over Notepad's default eight-point-ish text a 48-unit bar
   looks like a separate application parked above the document; dropping it to 35 by hand made it a squashed
   version of itself, because a single multiplier shrinks the type as fast as the padding.

## The catalogue

| Theme | Material | Density | Selection | Motion | Typeface |
|---|---|---|---|---|---|
| Fluent Surface | Windows translucent, tonal | Balanced | Filled accent pill, white text | Smooth | System sans |
| Spatial Glass | Frosted glass, specular edge, widest radius | Roomiest | Soft capsule of the same material | Softest spring | System sans |
| Command | Matte instrument — grey by day, near-black at night | Compact | Raised key + coral rule | Fast | System sans |
| Material You | Tonal, colour-forward | Balanced | Filled tonal container | Smooth | System sans |
| Paper & Ink | Warm cream, unruled | Balanced | Rust underline | Restrained | System sans |
| Editorial | Ivory, ruled, square, bordered | Balanced | Heavy ink underline | Nearly none | System sans |
| Terminal | OLED black, square | Compact | Block cursor, text knocked out | Instant | Cascadia / Consolas |
| Prism Rail | Barely there — a wash, no panel | Comfortable | Lit rail with a travelling dot | Most fluid | System sans |

**Prism Rail is the odd one.** Its selection is not on the candidate at all: a hairline rail runs under the
whole strip, lit from its left end to a dot beneath the selected word, so the lit length also says where in
the list the selection sits. That is why it can afford to have almost no surface — the rail is the interface.
A faint wash remains so the words survive a photograph or a saturated page, and the separation floor lifts it
further when the backdrop demands.

**Paper & Ink and Editorial are deliberately opposite readings of print.** One is a warm unruled page with a
rust pencil mark and soft elevation; the other is a ruled ivory column with a dark border, square corners and
a heavy ink rule. They differ in radius, rhythm, dividers, density and motion, which is what keeps them apart
in the identity check.

`tests\regression\Verify-ThemeIdentity.ps1` enforces the table: it reads the catalogue's tokens and fails if
any pair of themes differs in fewer than three dimensions, or differs mostly by hue — the grayscale test,
automated.

**Merged themes keep their stored numbers.** `BarTheme` is persisted as an integer, so the three that were
merged remain in the enum as `Legacy…` members and `AppSettingsStore.Migrate` maps them to their successors.
A user who chose Mica gets Fluent Surface, not a silent reset to the default. Numbers are never reused.

## Two rules learned from using it

**A dark variant is not a pale variant.** Spatial Glass's dark variant was a pale veil with dark text, which
over a dark editor read as tracing paper dropped on the screen. Dark glass is dark: a deep translucent body,
light text, and a selection that is a brighter piece of the same material. The same applies to Editorial,
whose night edition is deep ink with ivory type rather than an inverted page.

**A theme's two variants may be different designs, not one design at two brightnesses.** Command is a matte
instrument either way, but over a white page it is a cool grey slab with a white raised key, because a
near-black bar over a white document is a hole cut in the page. Terminal keeps OLED black over a dark editor
and moves to graphite over paper, for the same reason.

## The material must not drift

The backdrop is sampled whenever typing pauses, and no two samples of a page being typed into are identical:
a line scrolls under the sample points, a cursor blinks, a page repaints a shade lighter. Feeding those raw
numbers to the separation floor recomputed the surface slightly differently every time, which over a long
session is exactly the "it keeps subtly changing opacity and colour" the owner reported.

`Core/Presentation/BackdropTracker.cs` quantises the measurement into bands 0.20 wide and only leaves a band
when a sample clears its edge by 0.05. The floor therefore sees a handful of discrete values rather than a
continuum: the material is either what it was, or visibly and deliberately different. `BackdropTrackerTests`
asserts the negative case — a wobbling measurement changes nothing at all.

## Optical sizing

`Core/Presentation/OpticalSizing.cs` holds three densities, each **authored** rather than scaled from the
others, and interpolates between them for automatic sizing:

- The target height is `line height × 2.05 + 1.5`, clamped between the compact and comfortable ends. The
  multiplier was tuned against real environments, not derived: it puts Notepad's default text at the compact
  end, an eleven-point document at standard, and accessibility-scaled text at the comfortable end.
- The line height is estimated from the **caret**, which is the one measurement every host provides — a
  browser will not report a font size, and an editor reports whatever units it likes.
- **Each property travels at its own rate.** Type grows slowly (exponent 0.72) so it holds up when small;
  vertical padding grows quickly (1.25) because that is what makes a bar feel roomy; the shadow grows
  quicker still (1.4), since a small bar with a large shadow reads as a sticker; radii and gaps keep floors
  so a compact bar stays a rounded strip. `OpticalSizingTests` asserts these relationships rather than the
  numbers, so a retune cannot quietly turn it back into uniform scaling.

`OpticalSizer` adds hysteresis: a measurement has to move the caret by 12% and the resulting bar height by 3
units before anything changes, and losing sight of the caret changes nothing at all. The user should be able
to say "it picked a size for this", never "it keeps resizing".

**Diagnostics:** `WORDSTRIP_OPTICALLOG=1` writes one line per accepted size change to
`%TEMP%\wordstrip_optical.log` — caret, line height, DPI, and every resulting metric. Not exposed in
Settings; it exists for tuning the multiplier against applications.

## What the user sees

- **Bar size** replaces the thickness slider: Automatic, Compact, Standard, Comfortable. Automatic is the
  default and the only adaptive one; the other three are fixed and ignore the text entirely.
- Settings shows what automatic actually chose — "43 px tall, standard density · text about 19 px per line"
  — because a claim about the user's own screen should be checkable.
- The preview has its own density switch (Live / Compact / Standard / Roomy) that previews a density without
  changing the setting.
- An old `BarScale` that had been moved becomes the nearest fixed density; one left at its default becomes
  Automatic.
