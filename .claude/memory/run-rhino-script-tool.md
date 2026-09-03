---
name: run-rhino-script-tool
description: "The run_rhino_script LLM tool and its Rhino Document grounder — how they work, the RhinoCodePlugin load trap, and what a live signal trace showed about making the model's scripts reliable."
metadata: 
  node_type: memory
  type: project
  originSessionId: ac796705-55ea-4149-ac83-33c7950eca0c
  modified: 2026-09-03T07:09:44.336Z
---

2026-09-02/03. **BUILT and WORKING live in Rhino.** Physalia controls Rhino natively instead of
connecting to McNeel's MCP server — the decision and its evidence are in
[[rhino-mcp-vs-native-control]].

## The pieces

- **`Physalia.GH/Generation/RhinoScriptRunner.cs`** — the engine wrapper.
  `RhinoCode.Languages.QueryLatest(LanguageSpec.Python3)` → `ILanguage.CreateCode` →
  `Code.Run(RunContext)`. `Rhino.Runtime.Code` was ALREADY a compile-time reference (for
  `GhPythonBridge`), so no new dependency. `RunContext` is constructed with
  `defaultOutputStream/defaultErrorStream: true` **and then assigned our own `MemoryStream`s** — the
  flags make the engine allocate, the assignment points it at buffers we read back. Never throws for
  a fault in the script: `CompileException` (has `.Diagnosis`), `ExecuteException` (has `.Position`
  and `.StackTrace`), and anything else all return as a `ScriptOutcome`.
- **`Physalia.GH/Components/LlmTools/RunRhinoScript.cs`** — the node. `RunsAsync => true` marshalled
  to `RhinoApp.Idle` (same one-shot handler + `CancellationTokenRegistration` pattern as
  `TakeSnapshot.CaptureOnIdleAsync`). `Last Script` output published from `OnSolveEnd`.
  ComponentGuid `2F6A9C31-84D7-4B05-9E13-5C7A0D2E8B64`.
- **`Files/SYSTEM_PROMPTS/PREAMBLE/Rhino Scripting.txt`** — **carries NO schema, deliberately.**
  `SystemPrompt.Assemble` drops the entire JSON-contract paragraph when the schema is blank, which is
  the correct shape for a pipeline where the model answers in prose and calls tools. A schema file
  saying "emit nothing" would be worse than none. The Schema Picker must be REMOVED from the harness
  or it defaults to `C# Script.json`.
- **`Physalia.GH/Components/Grounding/RhinoDocumentGrounder.cs`** + Core's `RhinoDocumentGrounding` —
  see below. Guid `8A5E2C74-91F3-4B06-BD28-6E0C7A93F51D`.

## THE TRAP THAT COST A SESSION: RhinoCodePlugin loads on demand

`run_rhino_script` reported *"Rhino's Python 3 engine is not available in this Rhino installation"*
on a Rhino that has it. **`RhinoCodePlugin` (`c9cba87a-23ce-4f15-a918-97645c05cde7`) owns the script
languages and loads lazily** — normally the first time the Script Editor runs something. A Physalia
tool call is easily the first script of a session, so the registry held only the four languages
`Rhino.Runtime.Code` registers itself. The ids give it away:

```
Plain Text / JSON / Yaml / Git DotFile   rhinocode.builtin.*      ← always present
Python 3.9.10                            mcneel.pythonnet.python  ← only with the plug-in
IronPython 2.7.12                        mcneel.ironpython.python ←
```

**`WaitStatusComplete(spec)` does not save you** — it blocks only on languages the registry already
knows about, so with nothing Python-shaped enqueued it waits for nothing and returns instantly. The
fix is `PlugIn.LoadPlugIn(guid, loadQuietly: true, forceLoad: true)` FIRST, then
`WaitStatusComplete()` with **no** spec, then a bounded poll pumping `RhinoApp.Wait()`. Cache on
success only.

Note the irony: the MCP server's `run_python` never hit this, because driving the engine through
`_-ScriptEditor _Run` forces the plug-in load as a side effect. The out-of-process path this tool
replaces got for free the one thing the in-process path had to do deliberately.

## Decisions not to re-litigate

1. **Undo is owned explicitly** — `BeginUndoRecord`/`EndUndoRecord` with
   `RunContext.RecordDocumentUndo = false`, so the label is ours and the behaviour does not depend on
   an engine default nobody verified.
2. **The timeout bounds waiting for `RhinoApp.Idle` to ARRIVE, never the script.** Once the handler
   runs, the script owns the UI thread and no token takes it back — a managed thread cannot be
   aborted. A runaway script blocks Rhino exactly as it would in the Script Editor. Do not "fix" this
   with a worker thread; RhinoCommon off the main thread is strictly worse.
3. **Object count before/after is reported on EVERY path including failure.** Proved itself live: a
   call failed at `0 -> 7`, and the model continued from seven instead of rebuilding and doubling.
4. **No `get_context` tool.** With real streams, `print` IS the read-back. The MCP needs a 156-line
   context tool only because scraping the command window makes `print` unreliable.

## What a live signal trace taught (2026-09-02, "create a 3d house in rhino")

Seven tool calls, **four failures, all one class**: correct Python making the wrong .NET call.

```
rg.Box(Point3d, Point3d)               no such overload — needs rg.Box(rg.BoundingBox(p0,p1))
doc.Objects.CreateDefaultAttributes()  not a member of ObjectTable
ObjectAttributes                       AttributeError
mesh.Vertices.Add((x, y, z))           tuple is not a Point3f
```

Not one geometry or reasoning error. **Python's tolerance for near-enough types does not survive the
.NET boundary, and a Python-fluent model has no reason to expect that** — so the preamble now states
those four rules outright.

**My own preamble caused the blast radius.** It said to prefer one coherent script over a scatter of
tiny calls — sound for undo, and it turned every unverified signature into a total loss. Both big
failures died inside a helper the script then called twenty times: 4,785 chars spent to learn one
small fact, then 3,191 more. It now argues for grouping work already PROVEN and explicitly not for
gambling on work that is not, plus a "prove the API before you build with it" section. The model
reached the same conclusion unaided — but only on call five, after three rounds were gone.

**The real cure is a tool, not a prompt.** `search_rhinocommon` (`RhinoCommonSearch`) already ships
and describes itself as "the cure for invented method names". It was not wired into the harness —
every round read `1 tool(s)`. Prompt guidance makes a model guess more carefully; the tool means it
does not have to.

## Rhino Document grounder — its refresh is unlike every other grounder

Object count and kinds, layer table (hidden/locked noted), overall extents, and the SELECTED count
(so "move these" resolves). Exists to delete the probe round trip the trace opened with.

**GH expires along its own data graph, and a change to the RHINO document is not on it** — editing
geometry in Rhino runs NO Grasshopper solution anywhere, host or harness. So it watches thirteen
`RhinoDoc` events and each handler does exactly one thing: **`ExpireSolution(false)`**. Marking dirty
is ENOUGH because it sits upstream of the Conversation Log and the solve the user's next prompt
causes recomputes it first. A `ScheduleSolution` here is the [[script-io-grounder]] trap — a
sub-document is only re-enabled when its proxy solves and a disabled one silently drops scheduled
callbacks.

**No throttle, deliberately.** `CanvasStateGrounder` rate-limits because its watcher must serialize
the canvas before it can tell whether anything changed; this handler does no work at all, so 500
added objects cost 500 flag sets and one rescan. Units are deliberately excluded —
`DocumentUnitsGrounder` says that already. Capped at 25 layers / 8 type buckets. Crosses into Core as
strings and ints, since Core has no Rhino reference.

**Not yet run** — Rhino was closed before it could solve.
