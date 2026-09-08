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

## The SCENARIO set (added 2026-09-07, same branch)

Ten more, `S01`–`S10`, and the framing is the point: the numbered set teaches the PARTS, these
teach the WORK. Each is a job somebody has — record a procedure and repeat it, walk a scheme and
review it, interrogate the Rhino model, generate nodes, generate C#, get a second opinion, delegate
the legwork, check the model against a document, build site context from open data, keep a take-off
up to date. The last three are ones nobody asked for; they exist because the question a working
architect asks is not "what can it do to my canvas" but "what does it save me on Thursday".

`phybuild.core_loop()` was added first and is why this was affordable: it builds Chat + System
Prompt + Conversation Log + Model + LLM Call + the reply path in one call. Hand-wiring that ten more
times is how a Feedback ends up pointing at nothing.

### Three topologies the numbered set never used

- **S06 puts TWO Conversation Logs in one harness.** A pipeline normally has one. The join between
  writer and critic is ONE WIRE — the writer's `Success Signal` into the critic's `Prompt Signal` —
  because a signal carries its text, so an answer simply becomes the next question. Each half keeps
  its own System Prompt and its own history. `Signal Switch` on the word APPROVED decides whether
  the answer reaches the canvas or goes back as an objection. Verified: the critic's log assembled
  Instructions from the writer's signal, live.
- **S07 nests TWO harnesses**, each a complete pipeline with its own Chat. Both Delegate grip links
  and both inner documents survive the loader's id reissue (27 and 24 objects).
- **S10 puts a VALUE rather than prose on a Harness Out** (`Pipeline State`'s `Value`). That is what
  separates a tool from a chat about the same subject.

### The silent-failure class S10 found — now a standing check

Wiring a tool node's `Signal` to a Router output index **past the last tool slot** lands it on the
**Feedback** output. Nothing errors, no sweep sees it, the canvas looks right — and the tool is
never dispatched AND never advertised, so the model is told it does not exist. `router_slots(r, n)`
adds n slots to the default one, so `router_slots(r, 1)` gives TWO, and index 2 is Feedback.

`check_pairs.py` now walks every Router's last output in every preset and reports anything but a
Feedback sender on it — 44 slots across 24 presets, clean. It sits beside the two checks that were
already there for the same reason (a Feedback whose collector guid no longer resolves; a preset
carrying machine-specific text).

Two checker corrections found while there: **Set Script I/O links to EITHER script transmitter**
(the checker hard-coded Py Transmitter and called S05 broken), and `coverage.py` keyed presets on
`basename[:2]`, which made every scenario preset "S0".

### C# Transmitter RUN LIVE for the first time (S05)

Previously "built, not run in Rhino". Placed a real Rhino 8 C# Script component on the host canvas,
linked the transmitter, asked for a sum, and got working code in — `private void RunScript(double x,
double y, ref object a)` — with Schema Validator, C# Transmitter and Runtime Health Check all
Success. Two things confirmed: `IsLinkTarget` accepts `CSharpComponent` (`b6ba1144-…`) and REFUSES
the obsolete `Component_CSNET_Script` (`a9a8ebd2-…`), which is the `LanguageSpec` guard working; and
Set Script I/O reads its target THROUGH the transmitter's link, so pre-linking those two inside the
preset leaves the user only the one link that must be made on their own canvas.

**S04 also ran live** — the whole guardrail chain Success in one round, placing a slider + XY Plane
+ Circle + Panel grouped as "Circle at Origin". S04 and S05 between them prove both transmitter
paths.

### A broken PATH inside Rhino is indistinguishable from a broken Codex install

The other eight scenarios could not be live-run. In the Rhino process reached by the scripting
bridge, **`cmd.exe` cannot resolve ANY bare command name** — not `node`, not even `where` — although
`%PATH%` expands correctly (2331 chars, nodejs present) and `C:\Windows\System32\where.exe node`
finds node. Codex ships as a `.cmd` shim that runs `node`, so every Codex round dies with
`'"node"' is not recognized`. Claude Code is unaffected: it is a real `.exe` resolved by absolute
path. Not fixable from in-process (`Environment.SetEnvironmentVariable("PATH", …)` changes nothing),
survived a full Rhino restart from a PowerShell with a good PATH, and Codex ran fine earlier in this
project — so it is a session condition, not a Physalia regression. Worth remembering because the
symptom points squarely at the wrong thing; `codex --version` from a normal shell separates them.

### Component-name corrections found by building

- `Read PDF`'s input is **`Reference Folder`**, not `PDF Folder` as CLAUDE.md says, and it means the
  SHARED office library rather than a per-pipeline folder.
- `Folder Watcher` takes `Project Folder / Filter / Subfolders / Settle` — no `Instruction` input.
- `Signal Limiter` takes `Count` and emits `Within Limit` / `Over Limit`.
- `LLM Call` has no `Response` output; the reply text rides the signal, so read it with a
  Deconstruct Signal.
- `C# Transmitter` publishes no code output — the code is in the target component.
- The web tool node is `Read URL`, not `Read Url`.

### Bash-tool gotcha that cost several edits

In this environment a quoted heredoc (`<<'EOF'`) still collapses `\r\n` to `\r\n`, so a Python
patch script that means to match the literal text `\r\n` in a source file silently writes REAL
newlines into it instead. Use the Edit tool for anything containing backslash escapes, and
forward-slash paths inside heredocs to dodge `\U`/`\W` escape errors.

## The set is 28 (S11–S14 added, same day)

- **S11 Explain the Definition Nobody Documented** — inherit a file and get back what it does, which
  sliders matter, where it breaks. **Read-only on purpose**: no Drive Rhino, no transmitter. The
  first thing you do with somebody else's definition is understand it, and a tool that could
  rearrange it while you are still working that out is not what you want in the room. Canvas State is
  the whole trick — it hands over the real graph, so "which slider does nothing" is answerable.
- **S12 From a Sketch to a Massing** — the vision loop. Its argument is that the model must LOOK at
  what it built (`take_snapshot` after `run_rhino_script`); a model that never sees its own output
  will tell you confidently that it did what you asked. Image Mark Up is the underused half: an
  arrow is never ambiguous and takes four seconds.
- **S13 Run the Options Overnight** — the For Each preset, and the whole thing is ONE wire: `Next`
  comes from the END of the per-item work, so nothing advances until the current round has genuinely
  finished. Strictly sequential is not a limitation to work around — one Conversation Log downstream
  means parallel options would interleave into one thread.
- **S14 Can This Actually Be Made?** — the industrial-design one. Draft, wall thickness, undercuts,
  radii, sheet sizes, against rules the user writes. Two sentences in its brief are load-bearing
  whatever you make: **check the units FIRST** (a model in metres passes a 1.5 mm wall check for
  entirely the wrong reason) and **NOT-MEASURED is a good answer**, because a rule quietly skipped
  reads exactly like a rule passed.

`build_all.py` globbed `build_NN_*` only, so a whole-set rebuild silently skipped every scenario —
and reported "14", which looked right. Both families now, and it must push `ROOT` into each script's
exec scope or a run from another checkout rebuilds the wrong repo's presets.

**The toolkit is now machine-portable**: one `ROOT` bootstrap per script, `PRESETS`/`SCRATCH`
derived in phybuild. See [[preset-build-runbook]] — that is the file to read when asked to build
more.
