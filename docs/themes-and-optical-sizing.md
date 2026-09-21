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
| Fluent Surface | Windows translucent, tonal | Balanced | Tonal block + accent mark | Smooth | System sans |
| Spatial Glass | Luminous, milky, roomiest | Comfortable | Soft capsule | Softest spring | System sans |
| Command | Dark matte, near-opaque | Compact | Raised tonal key | Fast | System sans |
| Material You | Tonal, colour-forward | Balanced | Filled tonal container | Smooth | System sans |
| Paper & Ink | Warm opaque sheet, no glass | Balanced | Ink underline only | Nearly none | System sans |
| Terminal | OLED black, square | Compact | Block cursor, text knocked out | Instant | Cascadia / Consolas |

`tests\regression\Verify-ThemeIdentity.ps1` enforces the table: it reads the catalogue's tokens and fails if
any pair of themes differs in fewer than three dimensions, or differs mostly by hue — the grayscale test,
automated.

**Merged themes keep their stored numbers.** `BarTheme` is persisted as an integer, so the three that were
merged remain in the enum as `Legacy…` members and `AppSettingsStore.Migrate` maps them to their successors.
A user who chose Mica gets Fluent Surface, not a silent reset to the default. Numbers are never reused.

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
