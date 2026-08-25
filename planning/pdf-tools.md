# PDF Tools — authoritative

> Built 2026-08-25. Two components, used in conjunction: **Read PDF** under Human Tools (`AddPdf`)
> and **Read PDF** under LLM Tools (`ReadPdf`). This doc is the contract between them.
> `planning/tool-components.md` is a stale 2026-06 research doc referencing a `ToolComponentBase`
> that no longer exists — do not extend it.

## What problem this solves

Physalia could put images in front of a model and could not put a **document** in front of one. The
driving case is architectural drawing sets: 50–500 MB, tens to hundreds of sheets, usually vector
PDFs exported from Revit or AutoCAD.

Two facts about those files shape everything below.

1. **Most of what a drawing says is not in its text layer.** A section detail, a hatch, a callout
   bubble, a dimension against a line — text extraction answers "which sheet" and very little else.
   So the tool must be able to hand back a *picture*, which is why a native rasterizer is worth the
   compatibility cost it imposes (see CLAUDE.md → Build).
2. **They are far too big to put in a context window.** A forty-sheet set rendered eagerly is forty
   full-page images, and the conversation is over before the first question.

## The split, and why it is a split

| | Human tool (`AddPdf`) | LLM tool (`ReadPdf`) |
|---|---|---|
| Ribbon | Human Tools | LLM Tools |
| Wires to | Conversation Log → Human Tools | Router → Signal |
| Does | Grants PDF intake in the chat window | Answers `read_pdf` calls |
| Holds | Nothing — a marker record | The `PDF Folder` setting |

Attaching a PDF is **not** how its content reaches the model. The human tool registers the file and
puts one compact descriptor in the turn; the LLM tool pulls what is actually needed. A forty-sheet
set costs tens of tokens until somebody asks a question about it.

Either half alone is inert and says so: the Grounding panel's PDF row states that pages can only be
read if the LLM tool is also wired to a Router.

## The registry — how the two halves meet

`PdfRegistry` (`Components/LlmTools/PdfRegistry.cs`) is a `ConditionalWeakTable<GH_Document,
PdfSession>`.

- **Keyed by the local `GH_Document`.** A harness is one pipeline is one conversation, and the Chat
  that receives a drop and the Read PDF node that answers about it are both inside that same
  sub-document. Walking the wire from a tool node back to a Conversation Log would mean going
  through the Router and out the far side; this does not. A component sitting loose on the canvas
  still works, because the canvas is then the document they share.
- **Session-only. Nothing is persisted**, matching the rest of the lifecycle. A closed document
  takes its registry with it without anything having to remember to clean up.
- Two lists: `Attached` (everything this session) and `Pending` (picked but not yet announced).
  `DrainPending` is called once per send, so each attachment is announced exactly once.

## Intake — why the picker is host-side

**PDF bytes must never ride the `SubmitMessage.images` lane.** A 300 MB set base64-encoded through
`postMessage` is not a thing to do.

- **The PDF button** posts `phbridge://addpdf`; the host opens an Eto `OpenFileDialog` and registers
  the chosen paths. This is the only intake path that learns a file's **real location**, which is
  what "reference in place" requires, and it moves no bytes at any size.
- **Drag-and-drop** cannot give a path — the DOM File API withholds it — so it is the one path that
  does move bytes: base64 over the bridge under `kind: "pdf-drop"`, spooled to
  `%TEMP%/Physalia/dropped-pdfs/`. Capped at 100 MB, with the error pointing at the button.
- **The chip strip is separate from the image strip.** PDFs are never `PendingImage`s: there are no
  bytes to draw a thumbnail from, and `ImageEditor.svelte` loads its source by assigning `Image.src`
  and structurally cannot open a PDF. Keeping them apart is also what leaves the image lanes, the
  stale-attachment `$effect`, and the mark-up pencil untouched.
- **Probing happens at registration**, so a corrupt or encrypted file fails while a human is looking
  at it rather than surfacing three turns later as an unexplained tool error.

## The descriptor

Prepended to the prompt **text**, not added as a separate block — on the text it becomes the signal's
payload and flows through `WithPayloadText` like any other prompt, and a PDF sent with no typed
message still produces a turn with something in it.

```
[PDF attached]
  "A-101 Floor Plans.pdf" — alias `a-101-floor-plans`, 24 page(s), 841x594mm (A1), text layer on 22 of 24 pages.
    Sheet numbers (best guess, read from each page's title-block corner): p1 A-101 · p2 A-102 · …
Use the read_pdf tool to read these — nothing of their content is included above.
```

The sheet numbers come from `PdfTextReader.GuessTitleBlock`: the largest-font text in the page's
bottom-right eighth. **A heuristic, and reported as one in every message that carries it.** It earns
its place because it turns a page list into a sheet list, which is the difference between the model
asking for A-102 and paging through blindly.

## The zoom loop — the whole point of `render`

An ISO A1 sheet at 150 DPI is ~4900 px wide. The delivery cap (`ImageLimits.MaxImageSide`, 1568 px,
shared with `ViewportSnapshot` so both producers agree) reduces that by ~3x, and 4pt dimension text
lands at about seven pixels tall. Legible as a layout, illegible as text.

So the loop the tool description teaches is:

```
render (whole page)  →  see the layout
search "WALL TYPE"   →  page 3, region x=0.357 y=0.488 w=0.089 h=0.007
render (that region) →  601x155 px of just that note, fully legible
```

Two invariants make it work, and both are easy to break:

1. **`PdfRegion` is normalized 0–1 with a TOP-LEFT origin.** PdfPig reports glyph boxes measured *up*
   from the page bottom and PDFium's crop rectangle measures *down* from the top. The flip is
   expressed once, in `PdfRegion.FromPdfPoints` / `ToPointsTopLeft`, and nothing else does its own
   arithmetic. Getting it backwards renders the mirrored area — which on a title block looks like a
   plausible blank crop, not like a bug.
2. **`DpiRelativeToBounds: true` is mandatory whenever `Bounds` is set.** Without it, PDFium applies
   the DPI to the whole *page* and stretches the cropped content across a page-sized canvas: a tight
   crop comes back at the same pixel count as the full sheet, no more readable than before, and the
   zoom loop silently accomplishes nothing. Measured, not assumed — a crop at 96 DPI went from
   1121x792 (ink 32212, stretched) to the correct 778x312 with the flag on.

`render` always reports the delivered resolution and whether the image was downscaled, and a
full-page render additionally tells the model to crop rather than give up. Without that the model
reports it cannot read the drawing, which is true and useless.

## Never return an empty string for a scanned page

A scanned page and a blank page extract identically. `PdfTextResult.EmptyPages` exists so the caller
can always say which it is; `PdfReports.RenderText` turns it into an explicit sentence pointing at
`render`. This is load-bearing — without it the model concludes the sheet is empty and answers from
nothing.

## Actions

| Action | Notes |
|---|---|
| `list` | Attached + folder PDFs, with sheet guesses. Costs no page reads. |
| `text` | `pages` accepts `3`, `1-4`, `2,5,9`, `all`. Capped at 20 pages/call, `max_chars` default 8000 (matching `read_url`). |
| `search` | Case-insensitive, matches across word gaps (letters are grouped into baseline rows first). Returns page + region. |
| `render` | One page per call. `region` optional; `dpi` clamped 36–900, default 150. Returns an `ImageContent` attachment. |

Malformed arguments never throw: every failure arm in `PdfToolRequest.Parse` lands on a working
default, following `ReadUrl.ParseArgs`. A half-specified `region` is dropped entirely rather than
half-applied — a rectangle with two guessed edges shows the model the wrong part of the page
silently, where falling back to the full page shows it something it can correct from.

## The `PDF Folder` input

Input index **1** (`LlmToolComponentBase` registers Signal *first*, so appending shifts nothing in a
saved document — the opposite of `RoutingComponentBase`). Mirrors `MemoryTool`'s `Memory Folder`:

- A **bare name** → `Files/PDFS/<sanitized>`. Typed rather than derived, so it saves into the `.gh`
  and **travels inside a preset** — a pipeline can ship pointed at its own reference set.
- A **rooted path** → used verbatim, so a practice can point at a network share.

Sanitizing applies to the bare-name case only, and is where the `..` containment guard lives. Folder
documents are probed once and re-probed only when the file's timestamp changes — probing opens and
walks every page, so doing it per call on a folder of drawing sets would dominate the tool's cost.

## Threading

`RunsAsync => true`, but unlike `TakeSnapshot` there is **no UI-thread requirement** — nothing here
poses a viewport — so there is no `RhinoApp.Idle` marshalling, just a linked CTS with a 60 s timeout
as `ReadUrl` does. PDFium is **not** thread-safe: `ToolBatchRunner` serializes calls within one node,
but two Read PDF nodes on a canvas are not serialized against each other, so `PdfPageRenderer` holds
a static lock around every PDFium call.

`OnSolveTick` resolves the session and folder on the solve thread and caches them, because the call
itself runs off it and must not reach for the document or param values from a background thread.

## Icons

`Resources/AddPdf.png` and `Resources/ReadPdf.png`, 24x24, resolved by concrete type name and
embedded by the existing `Resources\*.png` glob.

Unlike the rest of the set — sliced from hand-drawn sheets by `tools/icons/Split.ps1` — these two are
generated by **`tools/icons/MakePdfIcons.py`**, which draws them at 16x and downsamples. Re-run it to
change them. It matches the house style measured off the shipped files rather than guessed at:
outline `#201E63`, cyan accent `#65CFDE` (magenta `#DE239B` is the set's third colour, unused here),
**transparent interiors**, and ink filling `x,y in [1,23]`.

Both share a folded-corner document and differ only in the cyan mark, which is the grammar the rest
of the set already uses: Add Image is a frame plus a cyan `+`, the search tools are a subject plus a
cyan magnifier. So the human intake tool takes the `+` and the model's reader takes the lens, and the
pair reads as two halves of one thing. The lens sits over a *document* rather than a chip, so it does
not collide with Component Search.

## The trap this shipped with

`App.svelte` and `Grounding.svelte` used `<FileTextIcon />` without importing it. The result was a
runtime `ReferenceError` **inside Svelte's render**, which aborts the rest of that pass — so the
window came up looking half-built and frozen, with the PDF button simply absent.

Nothing caught it. The C# side was correct and pushed `pdfToolWired: true`; the build succeeded; and
**`svelte-check` reported zero errors** (verified by re-breaking it deliberately). An unimported
component type-checks and compiles clean and only fails when that branch first renders.

Two guards now exist, and the static one is the cheap one to run:

- **`tools/uitest/check_components.py`** — flags any `<Component>` used in markup and never imported,
  across every `.svelte` file. One second, no browser. It knows about `import * as X`, `{#snippet}`
  and `{@const}`, so a clean run means something.
- **`tools/uitest/test_pdf_intake.py`** — drives the real bundle headlessly and asserts the button,
  the chips and their page counts and remove buttons. Its first assertion is that `window.__errs` is
  empty, because the thrown error is the disease and the missing button is only the symptom.

If a rail button ever fails to appear again, check `window.__errs` before checking the wiring.

## Known gaps

- **OCR.** A scanned set has no text layer, so `text` and `search` cannot serve it at all; the model
  has to `render` and read visually. That works, but it means no cheap full-document search on a
  scan. OCR would be a separate dependency and has not been taken on.
- **Class naming.** Both components display as "Read PDF" but the types are `AddPdf` and `ReadPdf`,
  because every GH component here shares one flat namespace and resolves its icon by type name.
