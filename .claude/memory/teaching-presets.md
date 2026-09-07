---
name: teaching-presets
description: "The annotated .phy teaching presets in wip_presets/, how they are generated from tools/presets/, and the provider and Grasshopper facts that building them uncovered."
metadata: 
  node_type: memory
  type: project
  originSessionId: 50b40ee3-470d-4ee3-9709-9df72e422950
  modified: 2026-09-07T10:39:06.954Z
---

2026-09-07. Built FOURTEEN annotated teaching harnesses in `wip_presets/`, on branch
`presets/demo-harnesses`. Each is a `.phy` a new user drops on a canvas and READS: numbered stages,
a blue heading and a yellow plain-English note per stage, laid out strictly left to right (only the
wireless return paths run backwards, and they are the ones that cannot be wires).

They are generated, not hand-wired. `tools/presets/` holds `phybuild.py` (the shared helpers),
`build_NN_*.py` (one per preset — re-run to regenerate its `.phy`), `verify.py` (reads a written
`.phy` back the way the loader does), `shoot.py` (renders a harness canvas to a PNG so a layout can
be LOOKED at), `liverun.py` (places a `.phy` and drives one real round with no chat window), and two
headless behaviour tests. Read `wip_presets/README.md` for the set and what each live run proved.

## Provider facts, all measured here

- **Claude Code cannot call Physalia's tools at all.** It ignores the tools argument, so the
  grounding tells the model they exist, it tries one, and it reports *"no such tool available"* —
  and NOTHING on the canvas looks wrong. Of the two keyless CLI providers only Codex can drive
  tools. Every tool-using preset therefore uses Codex and says so on the canvas.
- **Codex's tool DEFERRAL loses a large payload emitted in the same turn.** Preset 04 (canvas
  building) was first built on Codex with Memory and Component Search wired; the round died at
  Detect JSON with the model insisting the definition had *"already been emitted above"*. Codex
  defers a tool call and drops everything said after one, so a turn carrying both a tool call and a
  big JSON body loses the body. Fine when the answer is prose that comes AFTER the tool results
  (preset 03); fatal for a JSON-emitting pipeline. Presets 04 and 05 are tool-free on Claude Code
  because of it.
- **Do not pin a Codex model name.** `gpt-5.5` worked in four presets and then answered
  *"the model gpt-5.5 does not exist or you do not have access to it"*; the CLI's list also changed
  under us mid-session (5.5/5.4/5.4-mini → 5.6-sol/terra/luna/5.5/5.4-mini → back). An unpinned
  Picker snaps to whatever the CLI offers first and self-heals; a pinned name the account cannot use
  404s and does not.
- **Codex is slow** — two to four minutes per round in these tests. Claude Code answers in seconds.

## A product defect — found here, and FIXED here

**System Prompt re-placed a Picker on Preamble/Schema every time a file was READ**, and on its
SECOND solve that Picker snapped to `values[0]`. Measured: a plain conversational preset reloaded
with the 11,900-character *C# Script* preamble folded into its system prompt. `AddedToDocument` only
guarded on `GhJsonBridge.IsImporting`, which a preset load is not, so deleting the Picker — the
documented workaround — did not survive a save.

**Fixed 2026-09-07.** `PhyBase.Read` sets `WasRestored`, and the new `PhyBase.AutoPlacePicker` skips
a restored component — so auto-placing is finally what its own doc comment always claimed, something
that happens when you DROP a component on the canvas. The discriminator works because Grasshopper
deserializes an object by emitting it, calling `Read`, and only then adding it to the document; a
fresh placement never gets a `Read`. Eight components shared the defect (System Prompt, both CLI
models, Model API, ModelComponentBase, OpenAI-compatible, Token Estimator, Tokenization Techniques),
so the guard is central and a new component cannot get it wrong. Regression test:
`tools/presets/test_picker_reload.py` — ten assertions plus all eight components, including that a
fresh placement STILL gets its Pickers and a document saved with them reloads unchanged.

Still open, and separate: **`ApiCall` and `McpServer` tell the user to "right-click and add a
Picker" and no such menu item exists anywhere.** Those two never auto-placed one, so that promise
was already empty before this change.

Related, smaller: **`ImageSources`'s `/<alias>` prompt reference looks dead.** Nothing outside the
component and its own dialog reads it, and only PDFs have aliases in `ChatWindow`. Left out of the
presets rather than teaching something that does not work.

## Grasshopper scripting facts (extend [[building-harnesses-programmatically]])

- **`Attributes.Bounds` is whatever the constructor left there until `Layout()` has run**, and
  layout happens on SOLUTION, not on paint. An overlap check against a freshly scripted document
  reads every component as 150x20 at the origin and is meaningless. `layout()` forces it, and
  `overlaps()` is then a real test — it caught a dozen collisions the error sweep could not see.
- **A Panel used as a LIST input needs `Multiline` OFF.** Its data collection splits its one string
  by line only then; left on, three routes typed into a Declare node arrive as a single nonsense
  route, and the node's own Remark is the only sign. Hence `list_panel()`.
- **`GH_ButtonObject` has no `Value` — it is `ButtonDown`.** Assigning `Value` from Python succeeds,
  does nothing, and reads on the canvas as a trigger that never fires.
- **`place()` must take the ribbon SECTION.** Physalia has colliding names inside its own category:
  "Component Catalog" is both a Grounding component and a Params type, "Read PDF" is both an LLM
  tool and a human tool. Without the section, whichever proxy loaded first wins, silently.
- **`GH_Canvas.GenerateHiResImage` WRITES tiles and returns their paths** — it does not hand back a
  Bitmap. `GH_ImageSettings` is nested inside `GH_Canvas`, `TileSize` is read-only, and the tiles
  need stitching (`<col>;<row>.png`).
- **Nested harnesses work and are the intended shape for delegation.** `DelegateTool.Target()`
  resolves on the LOCAL document with the comment "a harness inside a harness — which is what lets
  the link be made by a drag at all". Preset 12 carries both documents in one `.phy`, the Delegate's
  link is remapped correctly through the id reissue a preset load performs, and a full delegated
  round RAN: Task In "1 received", the helper scripted its answer, Task Out "1 answered", `10` on
  the caller's Last Answer wire — with none of the helper's work in the caller's conversation.
- **The chain of `PresetLibrary.TryWritePackage(path, manifest, contents, files, out bytes, out
  error)`** is the dialog-free way to write a `.phy`; `PhyManifest.For` and `HarnessComponent.
  ReadDocumentFile` / `CreateWith` / `AdoptPackage` are the read side. All internal — reach them by
  reflection, and resolve types by full NAME across the Physalia assemblies, since ILRepack merges
  Core into `Physalia.GH` and there is no `Physalia.Core` assembly to ask.

## Two mistakes of mine, both worth not repeating

- **`Dispose()` on a read-back document is NOT enough — `RemoveObjects` first, then `Dispose`.**
  `RemovedFromDocument` is what releases a component's subscriptions, and a Rhino Document grounder
  holds THIRTEEN RhinoDoc handlers while a Project Folder holds a FileSystemWatcher. Reading the
  fourteen presets back several times over with a bare `Dispose()` left enough stale handlers that
  every RhinoDoc event fanned out to hundreds of dead grounders, and the session slowed until a
  TRIVIAL script could not finish inside the MCP call's 300-second window. `HarnessComponent.
  ApplyPackage` does it in the right order and CLAUDE.md already said why. `phybuild.retire()` now
  does too, recursing into a nested harness.
- **An MCP `run_python` call's RESPONSE times out at 300s, and the cancellation KILLS the script.**
  Not merely the reply: a preset build that overran was never written. So a long sweep must write
  its result to a FILE as it goes (`phybuild.write_log`), and anything expensive is better done
  outside Rhino entirely — a `.phy` is a zip whose `harness.gh` is RAW DEFLATE, so string-scanning
  the bytes needs no Rhino at all and takes under a second.
- **A check that cannot fail is worse than none.** The first audit scanned the COMPRESSED archive
  bytes, found none of the words that must be in every preset — not "Conversation Log", not
  "Physalia" — and pronounced all fourteen clean. Always make a scanner find something it
  definitely should before trusting a clean verdict; `audit.self_test` does that now.

## Runtime observations

- **Signal Limiter and Budget Guard both overrun their caps.** The limiter read `23 / 20` and the
  budget allowed 3 calls against `Max Calls` 2, because rounds are asynchronous and several can be
  in flight before any has been counted. The budget's own docs already say "can overrun by up to one
  call"; the limiter note in preset 08 now says the equivalent. Neither is an exact quota.
- **`runs.jsonl` and `conversation.json` really are written per turn** — read them off disk after
  the timer test.
