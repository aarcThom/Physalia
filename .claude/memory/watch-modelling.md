---
name: watch-modelling
description: "2026-09-06 — Watch Modelling records what the user does in Rhino as a repeatable procedure, built on Rhino's own command events rather than a geometry diff; it fires on DISARM, and the filtering lives in Core."
metadata: 
  node_type: memory
  type: project
  originSessionId: 87baa99a-bc2a-4051-b3ac-90fef7c24c3a
  modified: 2026-09-07T04:54:39.800Z
---

**Watch Modelling** (`ModellingWatch` in `Components/Triggers/`, `ModellingRecorder` in
`Core/Recording/`) records what the user does in Rhino and hands it to the model as a procedure to
repeat. Tick Recording, model it once, untick — one signal carries the whole demonstration.
**RUN LIVE IN RHINO 2026-09-06 and working**; 33 Core tests. A Watch-and-Repeat harness that uses it
is committed at `Files/PRESETS/User/Watch and Repeat.gh` (see [[building-harnesses-programmatically]]).

**Why:** "watch how I model this, then do the same to these" was the user's stated dream scenario.
The replay half already existed — `run_rhino_script` acts, `ask_human` with `rhino_selection` picks
the targets, For Each walks them, Budget Guard bounds the loop. What was missing was the RECORDING:
`RhinoTrigger` says "geometry was edited", not what you did.

**How to apply:**
- **Rhino already knows what you did — do not build a geometry differ.** Verified against the shipped
  RhinoCommon before building: `Rhino.Commands.Command.BeginCommand`/`EndCommand` give
  `CommandEnglishName` + `CommandResult`; `Command.UndoRedo` gives `IsBeginUndo`/`IsBeginRedo`;
  `RhinoApp.CapturedCommandWindowStrings(clearBuffer)` + `CommandWindowCaptureEnabled` give the
  parameters as text. A diff would need a full before-snapshot between rounds and recovers geometry
  rather than intent.
- **`BeforeTransformObjects`, NOT `AfterTransformObjects`.** The after-event carries only a
  `TransformEventId`; only the before-event carries `Transform`, `ObjectCount`, `Objects` and
  `ObjectsWillBeCopied`. That is the whole reason a gumball drag is recordable as a vector. (Note
  `RhinoDocumentGrounder` correctly uses the After event — it only needs to mark itself dirty.)
- **It fires on DISARM**, via the new `SignalSourceBase.FiresOnDisarm`, which suppresses the settle
  timer entirely: no pause length tells thinking apart from finishing. The base now has TWO disarm
  paths and the asymmetry is deliberate — `SetArmed(bool)` (the `IArmableTrigger` kill-switch form,
  used by `TriggerRegistry.DisarmAll`) DROPS the batch, while the node's menu calls
  `SetArmed(on, flush: true)`. Somebody pressing the panel's disarm-everything button is not asking
  for a round to start.
- **The filtering is the feature and it belongs in Core.** A real session is half navigation and half
  false starts, and a recording that teaches the noise is worse than none — the model reproduces the
  mistakes faithfully. What survives is judged by **EFFECT** (did the document change), which drops
  every view and selection command without a name list; the name list is a second pass for commands
  that DO mutate but are not the demonstration (Save, Options, Grasshopper, RunPythonScript).
- **Undo is the rule that matters most:** an Undo POPS the last surviving step and a Redo pushes it
  back. It does not mean "and then they undid it" — it means the step never happened.
- **A selection is not a step.** The selection at `BeginCommand` is recorded as that command's INPUT,
  which is what makes a step repeatable: "Offset" alone says nothing about what was offset.
- **`PipelineRhinoWrites` is [[trigger-tier]]'s file-write suppression one document over.**
  `run_rhino_script` and `RhinoGeometryTool`'s bake are not Rhino commands, so their object events
  arrive with no command open — indistinguishable from a gumball drag — and would be recorded as the
  user's own work. Both wrap their writes in a scope. Any future path that writes Rhino objects from
  the pipeline must do the same.
- **The default closing instruction makes the model describe the procedure back and WAIT.** A
  demonstration arrives as a user turn and the natural next move is to start applying it, with
  inferred parameters nobody checked.
- **The limit is not plumbing.** Command names are certain; parameters come from a text capture meant
  for a person, or from measuring the before/after. And repeating a demonstration on geometry that
  DIFFERS is the model generalising from one example — no component fixes that. A recording written
  to the Memory tool's local folder ships inside a `.phy`, which is what turns "watch how I do this"
  into a distributable firm asset rather than a session trick.

## What running it proved, and the two defects only running it could find

Armed it, ran Circle, Circle, Box, SelAll, Move, Undo, disarmed. Result:
`2 steps (4 cancelled or ineffective, 1 undone by the user and left out)` — the two Circles folded to
`Circle (x2)`, Box recorded as an Extrusion, the Move was popped by the Undo, SelAll never reached
the procedure. The model then replied through Claude Code, so the whole loop ran.

**1. `CapturedCommandWindowStrings(clearBuffer: true)` EMPTIES A SHARED GLOBAL.** Other plug-ins read
their own output from that buffer, and clearing it silently ate the Rhino MCP server's stdout —
every `run_python` came back `{"stdout": "", "error": null}`, successful and blank, for as long as the
recorder was armed. Nothing in the API says the buffer is shared and nothing complains when you take
it. **If MCP stdout goes silent mid-session, suspect a component you just armed.** Fixed by reading
with `clearBuffer: false` plus a high-water mark, resetting the mark if the buffer SHRANK (another
consumer cleared it). A partial transcript is the honest outcome of two destructive readers sharing
one global.

**2. The selection was reported as an input.** `3 Curve/Extrusion in` on a Circle reads as the circle
having been made FROM them; they were leftover selection from the previous step. Rhino never says
what a command CONSUMED, only what was highlighted when it started, so the wording is now
"selected". Generalises: anything derived from `GetSelectedObjects` at command start is context, not
provenance.

Both were invisible to a green build and to 32 passing tests. Both are committed and both **need a
Rhino restart to go live** — Rhino loads the `.gha` straight out of `bin` and holds a lock on it, so
the build's copy step fails while Rhino is open.

Related: [[trigger-tier]], [[run-rhino-script-tool]], [[intent-tools-declare-ask-human]],
[[signal-relay-conditionals]], [[memory-tool]], [[building-harnesses-programmatically]],
[[rhino-mcp-vs-native-control]].
