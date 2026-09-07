# Physalia component icons — generation prompts

Working document for the second icon pass (2026-08-17). The first pass (`2bcea90`) was a neon
8-bit sea-creature roster; this pass drops the nautical theme for **simple functional symbols**
drawn in the house style of the `phy_critter` mark.

Process recap — see memory `component-icon-generation`: generate sprite sheets, split them
programmatically, chroma-key the black, fit each sprite centred into a 24×24 PNG named
`<ClassName>.png` under `src/Physalia.GH/Resources/`. `PhyBase` resolves the icon by type name and
the `.csproj` embeds `Resources\*.png` by glob, so **no code changes are needed** — but restart
Rhino to clear Grasshopper's icon cache.

---

## The style (paste this preamble with EVERY sheet)

> **Style — read carefully, it is the whole point.**
> Draw every shape as a **beaded outline**: the line is a chain of overlapping circles, like a
> string of beads, with uniform bead diameter and softly rounded ends. Nothing is a smooth stroke.
> Shapes are **hollow** — outlines only, no fills, no gradients, no shading, no drop shadows, no
> glow, no highlights, no texture, no 3D. Flat vector, thick and chunky, the way a rubber-stamp
> toy looks. The bead diameter must be identical in every icon on the sheet and across sheets.
>
> Each icon is **one idea, drawn as few beads as possible** — it must still read as a 24×24 pixel
> icon, so no thin detail, no small text, no fine hatching, no more than about six elements.
> Composition is centred and roughly square, with generous empty margin on all four sides.
>
> **Palette — use these four colours only, no others, no tints, no blends:**
> - `#160A63` deep navy — the default line colour of every icon
> - `#83D2DE` pale cyan — accent
> - `#DE28C0` magenta — accent
> - `#4E285E` plum — accent
>
> Each icon uses navy plus **at most one** accent colour, applied to the one element that carries
> the meaning. Accent by family: **cyan = things that feed the model** (grounding, tools, models),
> **magenta = things the pipeline emits or writes out** (pipeline spine, signals, transmitters),
> **plum = machinery that inspects, gates or shrinks** (guardrails, control flow, tokens).
>
> **Sheet layout:** pure black `#000000` background. A strict, even grid of R rows × C columns
> with wide black gutters between cells — at least half a cell width of empty black between
> neighbouring icons, and a full black margin around the outside. One icon per cell, centred,
> filling about 70% of its cell. Do not draw grid lines, frames, borders, captions, labels,
> numbers, or any text anywhere in the image. Square 1:1 output at the highest resolution
> available.

**Attach `Images/phy_critter.svg` (or `src/Physalia.GH/Resources/critter.png`) as a style reference
image with every generation** — the bead language does not survive a text-only description, and
consistency between sheets is the thing that makes the set look like a set.

### Why sheets, not one image
Seventy-six icons in one generation gives ~110px per cell and the style drifts badly across the
image. Seven smaller sheets keep each icon large, keep the bead size stable, and let a bad sheet be
regenerated on its own. Grids below are **row-major reading order** — that order IS the cell →
filename mapping, so do not reorder the lists.

### Two traps from the last pass
- The sheet's grid was not perfectly even, which forced adaptive grid detection and per-cell
  connected-component isolation in the splitter. Wide gutters and a strict grid make the split
  trivial — insist on both.
- Intermediate `sprite_*.png` cells were left in `Resources\` and rode into the `.gha`. The csproj
  now carries `Exclude="Resources\sprite_*.png"`, but split to a scratch folder anyway.

### Not in this pass
- **`Chat`** — it overrides `Icon` with a per-instance Noto ocean emoji, so no sheet cell. It did
  later get a `Chat.png` (the lips mark) for the ribbon proxy alone — cut from `Images/scratch/lips.png`
  through the same splitter.
- **`brain.png`** — the fallback for any component with no icon. Sheet 2 cell 12 replaces it.

---

## Sheet 1 — Pipeline & Transmitters (9 icons, 3 columns × 3 rows)

*Accent: magenta `#DE28C0` throughout — this is the spine of the plug-in and everything that
writes out of it.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `HarnessComponent.png` | Harness | A rounded-square container holding three small beads joined left-to-right by a short beaded line — a pipeline in a box. The container outline is magenta, the pipeline inside is navy. |
| 2 | `SystemPrompt.png` | System Prompt | A page with one folded corner and two short horizontal lines of text on it. The folded corner is magenta. |
| 3 | `ConversationLog.png` | Conversation Log | Three stacked speech bubbles, alternating tail-left, tail-right, tail-left. The topmost (newest) bubble is magenta. |
| 4 | `LlmCall.png` | LLM Call | A single circle with four short straight rays radiating from it, one per quadrant — a pulse. The circle is magenta, the rays navy. |
| 6 | `ComponentTransmitter.png` | Component Transmitter | An arrow pointing right into a small component box that has two pins on its left edge — a graph being placed. The arrow is magenta. |
| 7 | `PyTransmitter.png` | Py Transmitter | An arrow pointing right into a rounded box containing a single S-curve, like a small snake. The arrow is magenta. |
| 8 | `CsTransmitter.png` | C# Transmitter | An arrow pointing right into a rounded box containing a sharp sign — two short vertical strokes crossed by two short horizontal strokes. The arrow is magenta. |
| 9 | `TextTransmitter.png` | Text Transmitter | An arrow pointing right into a rounded box containing two short stacked horizontal lines of text. The arrow is magenta. |

---

## Sheet 2 — Models (12 icons, 4 columns × 3 rows)

*Accent: pale cyan `#83D2DE`, except the two Anthropic entries and the fallback mark, noted below.
Every model component shares one base shape — a **chip**: a rounded square with three short pins
on its left edge and three on its right — and differs only by the mark inside it. Every tweaker
shares a **slider**: a horizontal beaded track with a round knob on it, and the same mark above.
Draw the chip and the slider identically every time; only the inner mark changes.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `AnthropicModel.png` | Anthropic Model | A chip with a six-spoke starburst inside it. The starburst is magenta `#DE28C0`. |
| 2 | `GeminiModel.png` | Gemini Model | A chip with a four-pointed sparkle inside it. The sparkle is cyan. |
| 3 | `OpenAICompatibleModel.png` | OpenAI Compatible Model | A chip with a hollow hexagon ring inside it. The hexagon is cyan. |
| 4 | `ClaudeCodeModel.png` | Claude Code Model | A chip with a terminal prompt inside it — a right-pointing chevron followed by a short underscore. The chevron is magenta `#DE28C0`. |
| 5 | `CodexModel.png` | Codex Model | A chip with a pair of angle brackets inside it, one pointing left and one pointing right, with a gap between them. The brackets are cyan. |
| 6 | `LlamaCppModelInfo.png` | LlamaCpp Model Info | A chip with a short three-segment bar chart inside it, the segments rising left to right. The bars are cyan. |
| 7 | `ModelInformation.png` | Model Information | A chip with a lowercase letter i inside it — a dot above a short vertical stroke. The i is cyan. |
| 8 | `AnthropicTweaker.png` | Anthropic Tweaker | A slider — horizontal track with a round knob — and a small six-spoke starburst floating above it. The knob is magenta `#DE28C0`. |
| 9 | `GeminiTweaker.png` | Gemini Tweaker | A slider with a small four-pointed sparkle floating above it. The knob is cyan. |
| 10 | `OpenAICompatibleTweaker.png` | OpenAI Compatible Tweaker | A slider with a small hollow hexagon floating above it. The knob is cyan. |
| 11 | `ApiKeys.png` | API Keys | A key seen side-on: a round hollow bow on the left, a straight shaft, two square teeth on the underside at the right. The bow is cyan. |
| 12 | `brain.png` | *(generic fallback)* | The Physalia critter itself, exactly as in the reference image — a dome-topped body with two round eyes, a small smile, and four straight tentacles below. All navy, no accent. |

---

## Sheet 3 — Grounding (9 icons, 3 columns × 3 rows)

*Accent: pale cyan `#83D2DE`. Every grounding icon sits on the same **ground line** — a short
horizontal beaded bar under the subject, drawn in cyan. Draw that bar identically in all nine.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `CanvasStateGrounder.png` | Canvas State | Three beads joined by two short lines into a small graph, sitting on the cyan ground line. |
| 2 | `PhysaliaGroupGrounder.png` | Physalia Group Components | A dashed rounded rectangle enclosing three beads, sitting on the cyan ground line — a group of components. |
| 3 | `ComponentCatalogGrounder.png` | Component Catalog | A three-by-three grid of small squares sitting on the cyan ground line. |
| 4 | `ClusterGrounder.png` | Cluster Grounding | A dashed rounded rectangle with two pins on its left edge and two on its right, sitting on the cyan ground line. |
| 5 | `DocumentUnitsGrounder.png` | Document Units | A short ruler — a long bar with three tick marks descending from its underside — sitting on the cyan ground line. |
| 6 | `ImageSources.png` | Image Sources | A picture frame containing a small triangle mountain and a round sun, sitting on the cyan ground line. |
| 7 | `PythonGrounder.png` | Python Grounding | A single S-curve, like a small snake, sitting on the cyan ground line. |
| 8 | `ScriptIO.png` | Script I/O | A rectangle with two pins on its left edge and two on its right, and a padlock shackle — a plain half-circle — arcing over its top. The shackle is cyan. |
| 9 | `ToolsInUse.png` | Tools Present | A wrench seen head-on — an open C-shaped jaw at the top of a straight handle — sitting on the cyan ground line. |

---

## Sheet 4 — Guardrails (10 icons, 5 columns × 2 rows)

*Accent: plum `#4E285E` — the machinery that inspects and gates.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `SchemaValidator.png` | Schema Validator | A page with a large check mark across it. The check is plum. |
| 2 | `GhDefinitionValidator.png` | GH Definition Validator | A shield outline with three beads joined by two lines — a small graph — inside it. The shield is plum. |
| 3 | `ComponentResolver.png` | Component Resolver | A hollow dashed box on the left, an arrow pointing right, and a solid component box with two pins on the right — an unknown name resolved to a real component. The arrow is plum. |
| 4 | `RequiredInputCheck.png` | Required Input Check | A component box with three pins on its left edge; the middle pin has no wire and ends in an open hollow ring. That empty ring is plum. |
| 5 | `FidelityCheck.png` | Fidelity Check | Two identical three-bead graphs side by side, the left one drawn dashed and the right one solid, with a short equals sign between them. The equals sign is plum. |
| 6 | `RuntimeHealthCheck.png` | Runtime Health Check | A component box with a heartbeat line — a flat line with one sharp spike — running across it. The spike is plum. |
| 7 | `DetectJson.png` | Detect JSON | A pair of curly braces facing each other with a single bead between them. The braces are plum. |
| 8 | `GeometryObservation.png` | Geometry Observation | An eye — a pointed oval — with a small cube in place of the pupil. The cube is plum. |
| 9 | `GeometryReport.png` | Geometry Report | A page with a small cube on it and a measuring bracket spanning the cube's width beneath it. The bracket is plum. |
| 10 | `StallGuard.png` | Stall Guard | A closed circular arrow loop with a single straight bar struck through it. The bar is plum. |

---

## Sheet 5 — Control Flow & Signals (12 icons, 4 columns × 3 rows)

*Accent: plum `#4E285E` for the five Control Flow icons (1–5), magenta `#DE28C0` for the seven
Signals icons (6–12). The compositors and decompositors are **mirror images** of one another —
arrows inward to compose, outward to decompose — so draw each pair as a matched set.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `Feedback.png` | Feedback | A single arrow curving back on itself to the left, like a U-turn. The arrowhead is plum. |
| 2 | `FeedbackCollector.png` | Feedback Collector | A funnel — a wide-mouthed V narrowing into a short spout — with two beads falling into its mouth. The funnel is plum. |
| 3 | `MergeSignal.png` | Merge Signal | Two beaded lines entering from the left, converging into a single line leaving to the right. The join point is a plum bead. |
| 4 | `SignalLimiter.png` | Signal Limiter | A beaded line running left to right, stopped by a vertical bar, with three tally marks above the bar. The bar is plum. |
| 5 | `BuildPlanTracker.png` | Build Plan | A checklist — three short horizontal bars stacked, the top two with check marks beside them and the bottom one with an empty box. The checks are plum. |
| 6 | `ConstructSignal.png` | Construct Signal | A bead with four short rays radiating from it and an arrow leaving to the right. The bead is magenta. |
| 7 | `DeconstructSignal.png` | Deconstruct Signal | A bead with four short rays, splitting into three short horizontal bars fanning out to the right. The bead is magenta. |
| 8 | `MessageCompositor.png` | Message Compositor | A small circle and a short bar on the left, two arrows pointing right, joining into one speech bubble. The bubble is magenta. |
| 9 | `MessageDecompositor.png` | Message Decompositor | A speech bubble on the left, two arrows pointing right, splitting into a small circle and a short bar. The bubble is magenta. |
| 10 | `ConversationCompositor.png` | Conversation Compositor | Three separate speech bubbles on the left, arrows pointing right, joining into one stacked block of three bubbles. The block is magenta. |
| 11 | `InstructionsCompositor.png` | Instructions Compositor | A page and a stack of two speech bubbles on the left, arrows pointing right, joining into one solid rounded block. The block is magenta. |
| 12 | `InstructionsDecompositor.png` | Instructions Decompositor | One solid rounded block on the left, arrows pointing right, splitting into a page and a stack of two speech bubbles. The block is magenta. |

---

## Sheet 6 — Tokens & Compaction, and Extra (12 icons, 4 columns × 3 rows)

*Accent: plum `#4E285E` for the eight Tokens & Compaction icons (1–8), magenta `#DE28C0` for the
four Extra icons (9–12). The four window/prune icons all share the same base — **a row of five
short vertical bars** — and differ only in what is done to that row. Draw the row identically in
all four.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `TokenEstimator.png` | Token Estimator | A row of small squares with a short semicircular gauge arc above them and a needle on the arc. The needle is plum. |
| 2 | `TokenizationTechniques.png` | Tokenization Techniques | A single long bar cut into three segments of different widths by two vertical breaks. The breaks are plum. |
| 3 | `TokenThreshold.png` | Token Threshold | A semicircular gauge arc with a needle, and below it a beaded line forking into two — one branch up, one down. The threshold tick on the arc is plum. |
| 4 | `SlidingWindow.png` | Sliding Window | A row of five vertical bars with a bracket enclosing only the three on the right. The bracket is plum. |
| 5 | `AnchoredWindow.png` | Anchored Window | A row of five vertical bars with a bracket around the leftmost bar and another bracket around the rightmost bar; the three in the middle are drawn as faint dashes. The brackets are plum. |
| 6 | `TokenWindow.png` | Token Window | A row of five vertical bars with a bracket around the right-hand group, and a small gauge arc above the bracket setting its size. The bracket is plum. |
| 7 | `ContentPruner.png` | Content Pruner | A row of five vertical bars with an X struck through the second and the fourth. The Xs are plum. |
| 8 | `Summarizer.png` | Summarizer | Four short bars on the left converging through arrows into one short bar on the right. The single bar is plum. |
| 9 | `Serializer.png` | Serializer | Three beads joined into a small graph on the left, an arrow pointing right, and a page marked with a pair of curly braces. The arrow is magenta. |
| 10 | `Deserializer.png` | Deserializer | A page marked with a pair of curly braces on the left, an arrow pointing right, and three beads joined into a small graph. The arrow is magenta. |
| 11 | `Picker.png` | Picker | A stack of three horizontal bars with a pointing hand — or a simple arrow cursor — resting on the middle one. The middle bar is magenta. |
| 12 | `ZoomGuid.png` | Zoom Guid | A magnifying glass — a circle with a short diagonal handle — held over a small component box, with a crosshair in the lens. The lens rim is magenta. |

---

## Sheet 7 — LLM Tools & Human Tools (12 icons, 4 columns × 3 rows)

*Accent: pale cyan `#83D2DE` throughout — everything here feeds context to the model or to the
human. Icons 1–7 are tools the **model** calls; 8–12 are buttons added to the chat window for the
**human**. The three magnifying-glass icons share one glass shape and differ only in what is under
it — draw the glass identically.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `Router.png` | Router | A single bead on the left with three beaded lines fanning out to the right, each ending in an arrowhead. The hub bead is cyan. |
| 2 | `WebSearch.png` | Web Search | A magnifying glass held over a globe — a circle crossed by one horizontal and one vertical curve. The lens rim is cyan. |
| 3 | `ComponentSearch.png` | Component Search | A magnifying glass held over a small component box with two pins. The lens rim is cyan. |
| 4 | `RhinoCommonSearch.png` | RhinoCommon Search | A magnifying glass held over a pair of curly braces. The lens rim is cyan. |
| 5 | `ReadUrl.png` | Read URL | A browser window — a rectangle with a separate strip across its top — containing two short lines of text, with one chain link beside it. The link is cyan. |
| 6 | `MemoryTool.png` | Memory | A short stack of two horizontal cylinders, like a small database, with a bookmark ribbon hanging over the front edge. The ribbon is cyan. |
| 7 | `RhinoGeometryTool.png` | Rhino Geometry | A wireframe cube in simple isometric view with a four-pointed sparkle at its upper right. The sparkle is cyan. |
| 8 | `AddImage.png` | Add Image | A picture frame containing a triangle mountain and a round sun, with a small plus sign at its lower right. The plus is cyan. |
| 9 | `GeometrySnapshot.png` | Geometry Snapshot | A camera — a rounded body with a small bump on top — with a wireframe cube inside the round lens. The lens rim is cyan. |
| 10 | `ViewSnapshot.png` | View Snapshot | The same camera with an eye inside the round lens instead of a cube. The lens rim is cyan. |
| 11 | `ExportConversation.png` | Export Conversation | A page with two short lines of text and a downward arrow leaving its bottom edge. The arrow is cyan. |
| 12 | `SignalTrace.png` | Signal Trace | A window frame — a rectangle with a strip across its top — containing a zigzag pulse line. The pulse is cyan. |

---

## After the sheets come back

1. Split to a **scratch folder**, never straight into `Resources\`.
2. Adaptive grid detection is still worth keeping as a fallback, but a strict grid with wide
   gutters should split on a plain even division. Per-cell connected-component isolation drops any
   sliver of a neighbour.
3. Chroma-key **pure black** to transparent. Key on black specifically — the darkest palette
   colour is `#160A63`, comfortably clear of `#000000`, so a tolerance of roughly 10% is safe and
   the navy outlines survive.
4. Fit each sprite centred into 24×24, preserving aspect.
5. Name `<ClassName>.png` exactly as tabulated, copy into `src/Physalia.GH/Resources/`,
   `dotnet build src/Physalia.slnx -c Debug`, restart Rhino.
6. Sanity check before building: 75 component icons + `brain.png`, plus `Chat.png` — the lips
   mark, which is the RIBBON button only (a placed Chat still wears its own ocean emoji).

---

## What actually happened (2026-08-17)

All seven sheets came back usable and every one of the 76 icons was generated. Three things did
**not** match what the prompts asked for, and each one matters for the next pass:

- **The returned files are numbered in REVERSE of the sheets above.** `Images/scratch/sheet1.png`
  is this doc's Sheet 7 (LLM & Human Tools); `sheet7.png` is Sheet 1 (Pipeline & Transmitters).
  Map sheets by CONTENT, never by filename.
- **No sheet is on an even grid.** Actual row layouts were 4/3/5, 4/4/4, 4/3/2/3, 5/5, 3/3/3,
  4/4/5, 3/3/3 — the "strict even grid" instruction was ignored. Row-major reading order *was*
  respected throughout, so the name lists mapped straight across.
- **Sheet 2 (Models) came back with a 13th icon** — a blank chip with no mark inside, in row 2
  position 4. Discarded.

Everything else held: pure black background, the bead language, the palette, and the shared base
shapes (chip, slider, ground line, five-bar row, magnifying glass, mirrored compositor pairs).

### The splitter lives in `tools/icons/`
`Split.ps1` (one sheet → named 24×24 PNGs), `Run.ps1` (the sheet → layout → names table for this
pass), `Contact.ps1` (magnified contact sheet for eyeballing the result on canvas grey). Run
`Run.ps1 -ReportOnly` first to check the segmentation before writing anything.

Two techniques in there worth keeping:

- **Segment by projection gaps, not by grid and not by connected components.** An icon is often
  several disconnected blobs — a grounder's body and its cyan ground line, a transmitter's arrow
  and its box — so "keep the largest blob per cell" tears icons apart. `Split.ps1` takes the ink
  profile, then merges across the smallest gap until the segment count equals the layout read off
  the image. That is self-verifying: a wrong layout throws instead of silently mis-slicing.
- **Downscale first, key second.** The sheet is already premultiplied against black, and
  downscaling premultiplied is correct; keying first makes GDI+ interpolate RGB and alpha
  independently and fringes every edge. Alpha is then recovered by matching each pixel's
  chromaticity to the nearest palette entry and dividing by *that* entry's peak channel — navy
  peaks at 99 and cyan at 222, so a single luminance threshold would render every navy icon at
  39% opacity.

---

## Additions after the second pass — drawn, not generated

A single new component does not justify a sheet: the bead language survives generation only when a
whole sheet is made at once, and a lone regenerated cell comes back at a different bead size.
Instead the icon is **drawn in code**, in the same palette, at 20× on pure black, and put through
the same two-step keying the splitter uses (downscale first, then alpha from the palette entry's
own peak channel — separately per colour layer, so navy and cyan never blend into one another's
alpha). At 24 px the bead texture is not resolvable anyway: what survives is a ~2 px flat line, and
matching that is what makes an addition sit in the set.

| File | Component | Icon |
|---|---|---|
| `TokenCount.png` | Token Count | The chat-window frame — a rounded rectangle with a strip across its top, the same shape as `SignalTrace` and `ReadUrl` — containing a semicircular gauge arc with a needle. The needle is cyan. |

The drawing script for it is not kept in the repo (it is fifty lines of `System.Drawing` and the
recipe above is the part that matters). Two things to reuse if another one is drawn this way:

- **One black-backed layer per colour, keyed by that colour's peak channel, composited afterwards.**
  Keying a single mixed layer means classifying each pixel's chromaticity, and the pixels where
  navy meets cyan classify wrong.
- **Nothing thinner than about 1.8 units on the 24 grid.** Two 1.2-unit dots in the window's title
  strip — drawn first, to echo `SignalTrace`'s title marks — came out as a pair of smears and were
  dropped; an empty strip still reads as a window.

## Pending — no icon yet

**Superseded by the third pass below, which carries these five prompts into Sheets C and D
unchanged apart from the two grounders' ground line.** The count here was also wrong: an audit on
2026-09-06 put the real figure at **29** components falling back to the brain, not two — see the
third pass for how it was counted.

`RunRhinoScript` and `RhinoDocumentGrounder` ship with no `Resources/<TypeName>.png`, so
`PhyBase.Icon` falls back to the generic brain. Neither is broken, only unlabelled; draw them in the
next pass.

| File | Component | Prompt |
|---|---|---|
| `RunRhinoScript.png` | Drive Rhino | A pair of curly braces with a small rightward play triangle between them, standing on a short horizontal ground line. The play triangle is cyan. Reads as "run this code" and stays distinct from `RhinoCommonSearch.png`, which puts a magnifier over braces rather than a triangle. |
| `RhinoDocumentGrounder.png` | Rhino Document | A document page seen face-on with one folded corner, three stacked horizontal bars across its lower half standing for the layer rows, and a small solid cube resting on the topmost bar. The cube is cyan. Reads as "what is in the file" and stays distinct from `CanvasStateGrounder.png`, which is about the Grasshopper canvas rather than the Rhino document. |
| `ProjectFolderGrounder.png` | Project Folder | A folder seen face-on with its tab on the upper left, and a small solid dot at its lower-right corner standing for the files inside. The dot is cyan. Reads as "this pipeline has a place of its own" and stays distinct from `MemoryTool.png`, which is about notes rather than files. |
| `DownloadFile.png` | Download File | A downward arrow landing on a short horizontal tray line, with a small cloud outline above the arrow tail. The arrow is cyan. Reads as "fetch it to disk" and stays distinct from `ReadUrl.png`, which is a page rather than a tray. |
| `ReadFile.png` | Read File | A document page with one folded corner and three short horizontal lines of text, with a small magnifier resting over its lower-right corner. The magnifier is cyan. Reads as "look inside a file here" and stays distinct from `ReadPdf.png`, which carries the PDF wordmark. |

---

# Third pass — the 29 unlabelled components (2026-09-07)

Everything built on the `events-and-delegation` branch shipped without an icon. Counted against the
built `.gha` rather than estimated: every type declaring `override Guid ComponentGuid`, matched
against the assembly's embedded `Physalia.GH.Resources.<TypeName>.png` names. **29 ribbon components
fall back to `brain.png`.** The `Param_*` types are exempt — `PhyParam` sets `GH_Exposure.hidden`,
so they never reach the ribbon and never need one.

This is an **additive** pass, which is a different problem from the second one. There the whole set
was made at once, so the bead size only had to be consistent within the generation. Here 29 new
icons have to sit beside 76 existing ones, and the note at the end of the second pass is explicit
that a lone regenerated cell comes back at a different bead size. Hence:

> **Attach TWO reference images with every sheet:** `Images/phy_critter.svg` for the bead language,
> **and a contact sheet of the existing icons** — build one with
> `tools/icons/Contact.ps1 -Dir src\Physalia.GH\Resources -Out sheet.png -Zoom 6`. Say in the prompt
> that the new icons must match the line weight and bead diameter of the attached set. Bead-size
> drift between passes is the one failure that cannot be fixed in the splitter, and it is what will
> make an addition read as foreign.

Paste **the style preamble from the top of this document** with every sheet, unchanged. Four sheets,
grids given per sheet, **row-major reading order is the cell → filename mapping** — do not reorder
the lists.

---

## Sheet A — Triggers (6 icons, 3 columns × 2 rows)

*Accent: magenta `#DE28C0`. Every icon on this sheet is a **signal source**, and they share one
base — **the mint**: a bead with four short rays radiating from it and a short arrow leaving to the
right, exactly as in the existing `ConstructSignal.png`. Draw the mint identically in all six, in
magenta, at the right of the cell. The symbol beside it — what the trigger watches — is **navy**, so
the mint carries the sheet's only accent and each icon still obeys "navy plus at most one accent".*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `TimerTrigger.png` | Timer | A clock face — a circle with two straight hands, one short and one long — with the mint at its right. Reads as a round starting on a schedule. |
| 2 | `FolderTrigger.png` | Folder Watcher | A folder seen face-on with its tab on the upper left, with the mint at its right. Stays distinct from `ProjectFolderGrounder.png`, which puts the same folder on a ground line instead of beside a mint. |
| 3 | `RhinoTrigger.png` | Rhino Changed | A wireframe cube in simple isometric view, with the mint at its right. The cube is the one from `RhinoGeometryTool.png`. |
| 4 | `DataTrigger.png` | Data Changed | Three beads joined by two short lines into a small graph — the same graph as `CanvasStateGrounder.png` — with the mint at its right. |
| 5 | `ModellingWatch.png` | Watch Modelling | A simple mitten hand with one extended finger, its fingertip touching the top face of a wireframe cube, with the mint at its right. Reads as "what the person did by hand", which is exactly what this records. |
| 6 | `ConstructToolCall.png` | Construct Tool Call | The mint alone, with an open C-shaped wrench jaw — the head from `ToolsInUse.png` — resting just above its bead. Stays distinct from `ConstructSignal.png`, which is the same mint with nothing above it. |

---

## Sheet B — Control Flow, the conditional layer (6 icons, 3 columns × 2 rows)

*Accent: plum `#4E285E` — machinery that inspects and gates. All six share one base — **the signal
line**: a horizontal beaded line running left to right across the cell, drawn in navy, the same line
as in the existing `SignalLimiter.png`. Draw it identically in all six; only what acts on it changes,
and that is the plum element.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `SignalGate.png` | Signal Gate | The signal line interrupted by two short vertical bars standing apart like an open gate, one reaching up from below the line and one down from above. The gate bars are plum. Stays distinct from `SignalLimiter.png`, which stops the line dead with a single bar and counts tallies above it. |
| 2 | `HoldSignal.png` | Hold Signal | The signal line interrupted by an hourglass — two short triangles meeting point to point. The hourglass is plum. |
| 3 | `SignalSwitch.png` | Signal Switch | The signal line arriving at a bead and forking into two lines leaving to the right, the upper ending in an arrowhead and the lower in a plain bead, with a small five-armed asterisk above the fork. The asterisk is plum — it is the pattern being matched. |
| 4 | `SignalThrottle.png` | Signal Throttle | The signal line pinched to a narrow neck by two opposing arcs, one bead passing through the neck and two more queued behind it. The arcs are plum. |
| 5 | `ForEachSignal.png` | For Each | A vertical stack of four short horizontal bars with a circular arrow loop to their left and one arrowhead pointing at the second bar down. The loop is plum. Stays distinct from `StallGuard.png`, whose loop is struck through by a straight bar. |
| 6 | `BudgetGuard.png` | Budget Guard | The signal line stopped by a vertical bar, with a semicircular gauge arc above it — the arc from `TokenEstimator.png` — its needle swung close to the far end. The bar and needle are plum. Reads as "the budget is nearly spent". |

---

## Sheet C — LLM Tools (11 icons, 4 columns × 3 rows)

*Accent: pale cyan `#83D2DE` throughout — everything here feeds the model or acts on its behalf.
Eleven icons in twelve cells: leave the last cell of the bottom row **empty black**. Icons 3, 4 and
5 are carried unchanged from the "Pending" section above.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `ApiCall.png` | API Call | A globe — a circle crossed by one horizontal and one vertical curve, the same globe as `WebSearch.png` — with a short beaded line leaving its right side and ending in an arrowhead. The globe is cyan. Stays distinct from `WebSearch.png`, which holds a magnifier over that globe instead of sending a line out of it. |
| 2 | `McpServer.png` | MCP Server | A two-pin plug on the left seated into a matching socket on the right, with three short lines fanning out from the socket's far side. The plug is cyan. Reads as "one connection, many tools", which is what makes this the only node advertising a set. |
| 3 | `DownloadFile.png` | Download File | A downward arrow landing on a short horizontal tray line, with a small cloud outline above the arrow tail. The arrow is cyan. Reads as "fetch it to disk" and stays distinct from `ReadUrl.png`, which is a page rather than a tray. |
| 4 | `ReadFile.png` | Read File | A document page with one folded corner and three short horizontal lines of text, with a small magnifier resting over its lower-right corner. The magnifier is cyan. Reads as "look inside a file here" and stays distinct from `ReadPdf.png`, which carries the PDF wordmark. |
| 5 | `RunRhinoScript.png` | Drive Rhino | A pair of curly braces with a small rightward play triangle between them, standing on a short horizontal ground line. The play triangle is cyan. Reads as "run this code" and stays distinct from `RhinoCommonSearch.png`, which puts a magnifier over braces rather than a triangle. |
| 6 | `TakeSnapshot.png` | Take Snapshot | The camera from `GeometrySnapshot.png` — a rounded body with a small bump on top — with a four-pointed sparkle inside its round lens. The sparkle is cyan. The sparkle is what marks this as the MODEL's own look, against the human's cube in `GeometrySnapshot.png` and eye in `ViewSnapshot.png`; draw the camera body identically to both. |
| 7 | `MoveInSpace.png` | Move In Space | A three-by-three grid of beads with a stepped path of two arrows running through it from the lower left to the upper right. The path is cyan. Reads as "one step at a time through a lattice". |
| 8 | `AskHuman.png` | Ask Human | A round head with a curved shoulder line beneath it, and a speech bubble beside the head containing a question mark. The question mark is cyan. |
| 9 | `DeclareTool.png` | Declare | A beaded line arriving from the left at a hub bead, with three lines leaving to the right — the topmost ending in an arrowhead, the other two ending in plain beads. The arrowhead is cyan. Reads as "one route of three, chosen", and stays distinct from `Router.png`, where all three lines end in arrowheads. |
| 10 | `DelegateTool.png` | Delegate | A rounded-square container on the right holding two beads — the container from `HarnessComponent.png` — with an arrow entering its left edge and a second arrow curving back out below it to a bead on the left. The returning arrow is cyan. Reads as "handed over, and answered". |
| 11 | `PipelineState.png` | Pipeline State | Three short horizontal bars stacked, each with a single bead sitting just off its left end — three key-and-value rows. The middle row's bead is cyan. Stays distinct from `MemoryTool.png`, which is a database stack with a bookmark ribbon. |

---

## Sheet D — Grounding, harness I/O and Human Tools (6 icons, 3 columns × 2 rows)

*Mixed accents, stated per icon — this sheet is the leftovers of three families, so unlike the others
it has no single accent. Icons 1 and 2 are the "Pending" prompts **with one change**: both now sit on
the cyan ground line that Sheet 3 of the second pass draws identically under all nine grounders, and
their own marks drop to navy so each icon still carries one accent. Without that, two grounders would
be the only ones in the set standing on nothing.*

| # | File | Component | Icon |
|---|---|---|---|
| 1 | `ProjectFolderGrounder.png` | Project Folder | A folder seen face-on with its tab on the upper left and a small bead at its lower-right corner standing for the files inside, sitting on the cyan ground line. The folder and bead are navy; the ground line is the accent. Reads as "this pipeline has a place of its own" and stays distinct from `MemoryTool.png`, which is about notes rather than files. |
| 2 | `RhinoDocumentGrounder.png` | Rhino Document | A document page seen face-on with one folded corner, three stacked horizontal bars across its lower half standing for the layer rows, and a small wireframe cube resting on the topmost bar, sitting on the cyan ground line. Page, bars and cube are navy; the ground line is the accent. Reads as "what is in the file" and stays distinct from `CanvasStateGrounder.png`, which is about the Grasshopper canvas rather than the Rhino document. |
| 3 | `HarnessIn.png` | Harness In | The rounded-square container from `HarnessComponent.png` with a straight beaded line entering through its left edge from outside the cell and ending in a bead just inside. The entering line is cyan. Deliberately **no mint** — this one is passive: data arrives and waits to be read, and starting no round is the whole point of it. |
| 4 | `TaskIn.png` | Task In | The same container with an arrow entering through its left edge and a bead with two short rays sitting just inside it. The rays are cyan. Those rays are the one difference from `HarnessIn.png`, and they carry the meaning: a task arriving DOES start a round. |
| 5 | `TaskOut.png` | Task Out | The same container with an arrow leaving through its right edge, carrying a single bead along its shaft. The arrow is magenta `#DE28C0` — magenta rather than cyan because this writes out of the harness, pairing it with the existing `HarnessOut.png`. |
| 6 | `TriggerControl.png` | Trigger Control | The chat-window frame — a rounded rectangle with a strip across its top, as in `SignalTrace.png` and `TokenCount.png` — containing three short horizontal tracks, each with a round knob, the knobs at different positions along their tracks. The knobs are cyan. The knob is the one from the tweaker sliders; reads as a row of switches. |

---

## Notes for this pass

**Sheet A and Sheet B are the ones to check first.** Both hang on a shared base drawn six times over
(the mint; the signal line), and a sheet where that base drifts cell to cell is worth regenerating
rather than splitting — the families read as families or they read as noise.

**Expect all three of the second pass's surprises again**, none of which were fixed by asking:
sheets come back **numbered in reverse**, so map by CONTENT; the **strict even grid is ignored**, so
read the actual row layout off the image and pass it to the splitter; and an **extra blank cell**
may appear. Sheet C is *specified* with an empty twelfth cell, so on that sheet a blank is expected
rather than a fault — discard it by name placeholder either way.

**Split with `tools/icons/Run.ps1 -ReportOnly` first**, to a scratch folder, never straight into
`Resources\`. Downscale on black first and key second; alpha per palette entry, not per luminance.
All of that is unchanged from the second pass.

**Two of the 29 have no sheet cell and are drawn instead**, if generation returns them badly: nothing
here is a candidate for the `System.Drawing` route on principle, but `TriggerControl.png` is the
closest — it is a window frame plus three sliders, which is geometry code hits exactly, and the
recipe at the end of the second pass covers it.

**After the sheets land:** 29 files into `src/Physalia.GH/Resources/`, `dotnet build
src/Physalia.slnx -c Debug`, **restart Rhino** (Grasshopper caches icons). Then re-run the audit —
every type declaring `override Guid ComponentGuid`, minus the hidden `Param_*` types, matched against
the assembly's embedded resource names — and it should report **nothing** falling back except
`brain.png` itself and `Chat`, which overrides `Icon` by design.

---

## What actually happened (2026-09-07)

**All four sheets came back usable on the first generation, and none of the second pass's three
surprises recurred.** Filenames matched the sheets (`sheet_a` really was Sheet A), the grids were
even, row-major order held throughout, and Sheet C's twelfth cell came back empty exactly as
specified. Actual layouts: **3/3, 3/3, 4/4/3, 3/3**; the first three sheets 1254×1254, Sheet D
1536×1024. Whether the improvement came from attaching the contact sheet, from asking for the blank
cell rather than discovering one, or from a better model is not knowable from one pass — but ask for
all three again.

`Split.ps1` needed no changes: projection segmentation found all 29 cells, and every output was
24×24 with a transparent corner and real ink.

### The drift check said the opposite of what the eye said, and it was right

A side-by-side of eight new icons against the existing sibling each was drawn to match read clearly
as "the new ones are thinner". Measured — median horizontal run of opaque pixels as a stroke proxy,
plus total ink at alpha > 110 — it is **not true**:

| set | median stroke | mean stroke | mean ink | icons at stroke 1 |
|---|---|---|---|---|
| existing 80 | 2 | 1.86 | 130 | **17** |
| new 29 | 2 | 2.10 | 128 | **1** |

The new set is very slightly *heavier* than the old average. The eyeball comparison misled because
every obvious sibling to compare against — `SignalLimiter`, `StallGuard`, `Router`,
`ConstructSignal` — happens to sit at the heavy end of the existing set, so the honest comparison
was against outliers rather than against the set. **Measure this before changing the splitter**: the
"fix" would have been a change to shared code, applied to a problem that was not there.

One real outlier in the new set: **`SignalGate` at ink 37**, the sparsest of the 29 — it is a line
and two short bars and nothing else. Left as generated, because the existing floor is `ComponentResolver`
at 46 and `DetectJson` at 48, so it is within the set's own range and it reads at 24 px.

### Result

**0 components fall back to `brain.png`.** 108 ribbon component types, 131 embedded png resources
(the surplus is `brain`, `critter`, `Chat` and the bundled Noto emoji). The icon line in the
pre-ship sign-off table can be closed.

### One more PowerShell gotcha for the pile

**Variable names are case-INSENSITIVE**, so `$S` (a source directory) and `$s` (the loop variable
over the sheet table) are the same variable. The symptom is a nonsense path —
`...\System.Collections.Hashtable\sheet_a.png` — rather than an unset-variable error.
