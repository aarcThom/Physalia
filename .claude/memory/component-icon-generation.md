---
name: component-icon-generation
description: How Physalia's component icons are mass-generated — sprite sheets split programmatically — the install contract, and the splitter in tools/icons.
metadata:
  node_type: memory
  type: reference
---

Physalia's component icons were **mass-generated once, from ONE image**, not drawn per component
(commit `2bcea90`, 2026-07-06, "give all 56 components neon 8-bit sea-creature icons"). The split
script was ad hoc and never committed — this is the recipe to redo it.

**The source: a single 8x7 sprite sheet.** Chunky 8-bit deep-sea creatures on a strict bright
palette (neon green, pink, blue, cyan, yellow, coral + navy outline, white glints), laid out on a
**pure-black** background. The sheet itself is not in the repo.

**The split, in four steps** — each one exists because the naive version failed:
1. **Adaptive grid detection** — strong-gutter detection plus width-subdivision. A generated sheet
   is not an even grid; assuming one mis-slices it.
2. **Per-cell connected-component isolation** — keeps the largest blob so slivers of the
   neighbouring sprite bleeding into a cell get dropped.
3. **Chroma-key the PURE BLACK to transparent** — keyed on black *specifically*, because the
   sprites' own outlines are navy. A background flood-fill or an edge-based matte eats the outline.
4. **Fit centred into a 24x24 PNG**, named `<ClassName>.png`.

**Install contract — adding an icon needs NO code change.** Drop `<ClassName>.png` into
`src/Physalia.GH/Resources/`. `PhyBase.Icon` resolves `Physalia.GH.Resources.{GetType().Name}.png`
off the assembly manifest (an explicit `IconPath` field overrides the name), and
`Physalia.GH.csproj` embeds `Resources\*.png` by glob — `Exclude="Resources\sprite_*.png"`, a guard
added after the mishap below. A missing resource falls back to `Physalia.GH.Resources.brain.png`
(since 2026-09-07 **nothing does** — see the fourth case below).

**Three gotchas:**
- **Grasshopper caches icons — restart Rhino** or a replaced PNG never shows.
- **`Chat` is half-exempt.** It overrides `Icon`, so a placed Chat wears its per-instance ocean
  emoji. Since 2026-08-17 `Chat.png` (the lips mark) exists anyway and is used for the RIBBON
  proxy only — see [[chatbox-emoji-identity]].
- **Never leave intermediates in `Resources\`.** The glob embeds whatever is there: 22
  `sprite_r*_c*.png` cells rode into the `.gha` in `04b0fba` and were only removed in `2bcea90`.
  Hence the csproj `Exclude`.

**Tooling on this box** (the real constraint on any redo): no ImageMagick, Inkscape or
rsvg-convert, and no Python cairosvg/Pillow. The split and its verification — pixel format, corner
alpha, ink bounding box — go through `System.Drawing` in PowerShell; an SVG source is rasterized
with headless Chrome. See [[svg-rasterization-headless-chrome]].

**Not the same thing: the emoji bitmaps.** `Resources/emoji/emoji_u<codepoint>.png` are *bundled*
Noto Emoji (Apache-2.0), not generated — GDI renders only a colour emoji font's monochrome base
layer, so pre-made images were the only option. See [[chatbox-emoji-identity]].

Related: [[physalia-repo-gotchas]] (the `Files/` vs embedded-resource split).

---

## Second pass — 2026-08-17: the flat symbol set (current)

The sea-creature roster was replaced by **simple functional symbols in the critter's own bead
language**: every line is a chain of overlapping circles, hollow, flat. Palette `#160A63` navy
(the line of every icon) plus one accent — `#83D2DE` cyan feeds the model, `#DE28C0` magenta is
emitted or written out, `#4E285E` plum inspects/gates/shrinks. 76 icons: all 75 icon-bearing
components (nothing falls back any more) plus a new `brain.png`, and `Chat.png` — a lips mark
cut from `Images/scratch/lips.png`, worn by the ribbon proxy only.

**The prompts are `planning/component-icon-prompts.md`; the splitter is now IN THE REPO at
`tools/icons/`** (`Split.ps1`, `Run.ps1`, `Contact.ps1`) — the whole reason this note existed was
that the first pass's script was ad hoc and lost.

What generation actually returns, versus what it is asked for — assume all three next time:
- **Sheet files come back numbered in REVERSE** of the prompt order. Map by CONTENT, never by
  filename.
- **"Strict even grid" is ignored** (rows came back 4/3/5, 4/3/2/3, 5/5 …), though row-major
  reading order is respected. So layout must be read off the image and passed in.
- **Extra cells appear** — one blank chip with no mark. Discard by name placeholder.

Technique that made it robust:
- **Segment by projection gaps, merging across the smallest gap until the count matches the
  layout read by eye.** Self-verifying — a wrong layout throws rather than mis-slicing. Do NOT use
  per-cell connected-component isolation this time: an icon is often several disconnected blobs (a
  grounder's body plus its ground line, an arrow plus its box) and largest-blob logic tears them
  apart.
- **Downscale on the black background FIRST, key SECOND.** The sheet is premultiplied against
  black and downscaling premultiplied is correct; keying first makes GDI+ interpolate RGB and
  alpha independently and fringes every edge.
- **Alpha per palette entry, not per luminance.** Match the pixel's chromaticity to the nearest
  palette colour and divide by that colour's peak channel — navy peaks at 99, cyan at 222, so one
  luminance threshold renders navy at 39% opacity.
- **PowerShell gotcha that cost a debug cycle:** the comma operator binds tighter than `-`, so
  `@($s, $i - 1)` parses as `($s,$i) - 1` and throws "does not contain a method named
  op_Subtraction". Parenthesize: `@($s, ($i - 1))`.


---

## Third case — a lone icon, DRAWN not generated (2026-08-24)

`TokenCount.png` was added on its own. Do not regenerate a sheet for one icon: the bead size drifts
between generations and the addition then reads as foreign. Draw it in `System.Drawing` instead —
at 24 px the bead texture is not resolvable, so what has to match is a flat ~2 px line in the
palette, which code hits exactly.

Recipe (full write-up at the end of `planning/component-icon-prompts.md`):
- Render at 20x on **pure black**, one bitmap PER COLOUR, downscale to 24 with HighQualityBicubic,
  THEN key. Same order as the splitter, same reason.
- Alpha per layer from that colour's own peak channel (navy 99, cyan 222); composite source-over
  afterwards. Keying one mixed layer needs per-pixel chromaticity classification and the navy/cyan
  boundary pixels classify wrong.
- Nothing thinner than ~1.8 units on the 24 grid. Two 1.2-unit window-title dots came out as smears.
- Same install contract as ever: drop `<ClassName>.png` in `Resources\`, rebuild, restart Rhino.


---

## Fourth case — the additive pass (2026-09-07): 29 icons, and a drift scare that measured false

The `events-and-delegation` components all shipped iconless. **29** of them, counted against the
built `.gha` rather than estimated — the figure had been carried in the planning doc as 18, and the
audit is what caught it: every type declaring `override Guid ComponentGuid`, minus the hidden
`Param_*` types, matched against the assembly's embedded `Physalia.GH.Resources.<TypeName>.png`
names. Four sheets (Triggers 3x2, Control Flow 3x2, LLM Tools 4x4x3, Grounding+IO 3x2), prompts in
`planning/component-icon-prompts.md` under "Third pass". **Result: 0 fallbacks across 108 ribbon
types.**

**Everything the second pass warned about failed to happen this time.** Filenames matched the
sheets, grids were even, row-major order held, and the one blank cell was the one that had been
ASKED for. Two things were done differently and either may be why: a **contact sheet of the existing
80 icons** was attached as a second style reference alongside the critter (`Contact.ps1 -Dir
<Resources> -Zoom 6`), and the short sheet was *specified* with an empty last cell instead of
leaving generation to invent one. Ask for both again.

**The lesson worth keeping is about the drift check.** An additive pass has one real risk — new bead
size not matching the old set — so eight new icons were put side by side with the existing sibling
each was drawn to match, and it read unmistakably as "the new ones are thinner". **Measured, it was
false**: median horizontal opaque run and total ink came out new 2.10 / 128 against existing
1.86 / 130, and the OLD set has 17 icons at stroke 1 where the new set has one. The comparison had
been made against `SignalLimiter`, `StallGuard`, `Router` and `ConstructSignal` — every obvious
sibling happens to sit at the heavy end of the set, so the honest-looking check sampled outliers.
Had it been trusted, the "fix" would have been a change to `Split.ps1`'s shared scaling for a
problem that did not exist. **Measure stroke and ink before touching the splitter.**

`Split.ps1` needed no changes at all; projection segmentation found all 29 cells first time. The one
genuine outlier is `SignalGate` at ink 37, the sparsest of the 29 — left as generated, since the
existing floor is `ComponentResolver` at 46.

PowerShell, again: **variable names are case-insensitive**, so `$S` (a directory) and `$s` (a loop
variable) are one variable. It surfaces as a nonsense path, not an error.

**One component now borrows a sibling's icon (2026-09-09).** `LlamaCppApi` ("LlamaCpp API") sets
`PhyBase.IconPath` to `Physalia.GH.Resources.LlamaCppModelInfo.png` — the FIRST use of that hook,
which had existed unused. So the "0 brain fallbacks" state still holds, but it is 109 components
against 108 icons: the next additive pass should draw one for it, and `IconPath` is the pattern to
follow (and then delete) rather than shipping the generic brain while a sheet is pending.
