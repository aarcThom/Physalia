---
name: component-icon-generation
description: How Physalia's 56 component icons were mass-generated — one sprite sheet, split programmatically — and the install contract for adding more.
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
(as CsTransmitter, TextTransmitter, Harness and ScriptIO currently do).

**Three gotchas:**
- **Grasshopper caches icons — restart Rhino** or a replaced PNG never shows.
- **`Chat` is exempt.** It overrides `Icon` with its per-instance ocean emoji bitmap.
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
