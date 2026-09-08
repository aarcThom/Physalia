---
name: building-harnesses-programmatically
description: "How to construct a Physalia pipeline inside a harness from code (via the Rhino MCP's run_python), and the wiring rules that are not obvious until Grasshopper refuses them."
metadata: 
  node_type: memory
  type: reference
  originSessionId: ac796705-55ea-4149-ac83-33c7950eca0c
  modified: 2026-09-03T07:10:13.993Z
---

2026-09-02. Built a working Physalia pipeline inside a harness end to end from outside Rhino, by
driving the Rhino MCP's `run_python` tool. Useful for testing a new component without hand-wiring a
canvas, and the wiring lessons apply to authoring presets by hand too.

## The mechanism

The Rhino MCP's `g1_*` tools operate on the ACTIVE Grasshopper canvas, so they cannot reach inside a
harness. `run_python` can, because `HarnessComponent` exposes **`public GH_Document? InnerDocument`**
and **`public GH_Document EnsureInnerDocument()`**.

```python
harness = <find on Grasshopper.Instances.ActiveCanvas.Document by InstanceGuid>
inner   = harness.EnsureInnerDocument()
obj     = Grasshopper.Instances.ComponentServer.EmitObject(System.Guid(component_guid))
obj.CreateAttributes()                       # if Attributes is None
obj.Attributes.Pivot = System.Drawing.PointF(x, y)
inner.AddObject(obj, False)
dst_param.AddSource(src_param)               # wiring
inner.UndoUtil.RecordAddObjectEvent(name, lst)   # ONE Ctrl+Z for the whole build
inner.NewSolution(False)
```

Resolve component guids with the MCP's `g1_search_components` (`{"query": ..., "category":
"Physalia"}`), or scan `ComponentServer.ObjectProxies` by `proxy.Desc.Name` in the script itself.

**Always `RecordAddObjectEvent`.** A script that mutates a document without one leaves changes that
Ctrl+Z cannot lift. Learned by making a mess in a harness that already held 70 objects — always
check `InnerDocument.ObjectCount` before writing, and refuse if it is not what you expect.

## THE WIRING RULE THAT BITES: every backward path is wireless

Wiring a return path as an ordinary wire gets **`Error: Recursive data stream found, this component
depends on itself`** on the Conversation Log. The Feedback → Feedback Collector grip-link exists
precisely to break the GH DAG, and it is needed on EVERY backward hop, not just the tool one:

```
LLM Call . Success Signal  ~~>  Conversation Log . Response Signal
Router . Feedback          ~~>  Conversation Log . LLM Tool Signal
<tool> . Result            ~~>  Router . Results
```

`~~>` means Feedback component → `fb.AddCollector(fc.InstanceGuid)` → Feedback Collector → wire on.
`Feedback.AddCollector(Guid)` is **public**. Collectors are not shareable across different
destination inputs; give each return path its own pair.

Forward wires are ordinary: `System Prompt → ConvLog`, `Chat → ConvLog`, `Tools Present → ConvLog`,
`ConvLog → LLM Call`, `Model → LLM Call`, `LLM Call.Tool Calls → Router`, `Router.<toolname> →
<tool>.Signal`.

**Read the canonical wiring rather than guessing it.** A shipped preset can be opened without
touching the canvas:

```python
io = Grasshopper.Kernel.GH_DocumentIO(); io.Open(r"...\Files\PRESETS\Physalia\Claude Code - Python 3.gh")
for o in io.Document.Objects: ...   # walk Params.Input[].Sources for every wire
```

That is how the `Feedback Collector . Signal -> Conversation Log . Response Signal` shape was found.

## Things that surprised me

- **An "empty" harness is not empty** — it ships with a **Chat** at (0,0). Reuse it; a second Chat is
  wrong (the proxy wears one emoji per Chat, and the preset loader keys on having one).
- **System Prompt and Claude Code Model auto-place their own Pickers** on being added — 3 extra
  objects appeared unbidden. Designed behaviour, not a bug.
- **The Router renames its output slot after the tool it matched** (`T1` → `run_rhino_script`). That
  rename is the cheapest proof that a new tool node's `LlmToolDefinition` parsed and dispatch is
  wired correctly.
- **`Picker.SetSelectedValue` is `internal`** — reach it by reflection. And the order matters:
  `Picker.SolveInstance` resets to `values[0]` when the current value is not in the list, and the
  list is only repopulated by the OWNER's solve — so solve once, then set, then solve again.
- A pipeline built this way lives only in memory. **Save the .gh or it dies with the Rhino process.**

## Verifying without opening the UI

`inner.Objects` + `c.RuntimeMessages(GH_RuntimeMessageLevel.Error/Warning)` gives a full error sweep,
and walking `Params.Input[].Sources` prints the whole wire list. Reading each component's
`Params.Output[].VolatileData` confirms a tool is actually advertising
(`Run Rhino Script . Tool = "Tool: run_rhino_script"`, `Tools Present . Grounding = ToolsGrounding`).

See [[harness-subdocument]] for what a harness is, and [[run-rhino-script-tool]] for the component
this was built to exercise.

## 2026-09-06 — a second build, and the traps that were mine rather than Grasshopper's

Built the Watch-and-Repeat harness (21 components) the same way. The wiring rule above held exactly
as written — **read this file before wiring, not after**; the recursion error came back verbatim
because it was re-derived instead of recalled.

**`SetPersistentData` APPENDS to the default the component registered.** It does not replace. So
setting a boolean input that was registered with `true` leaves `[True, False]` — **two items on an
item-access input, which makes Grasshopper solve the whole component TWICE**. Every output came out
duplicated and the trigger minted its signal twice (same sequence number, two items on the wire).
Clear first — and the method is **`Script_ClearPersistentData()`**; `ClearPersistentData` does not
exist on the param.

**A string handed to `SetPersistentData` resolves to `IEnumerable<char>`** and stores ONE ITEM PER
CHARACTER — `"modelling-procedures"` became 20 items, so the component solved twenty times. Always
wrap: `p.SetPersistentData(System.Array[System.Object]([value]))`. Sweep for this after any scripted
build:

```python
for p in obj.Params.Input:
    if p.SourceCount == 0 and getattr(p, "PersistentData", None) and p.PersistentData.DataCount > 1:
        print("DOUBLED", obj.Name, p.Name)   # not every param HAS PersistentData - guard with getattr
```

**Placing a harness from a script points the canvas INTO it.** After `AddObject` +
`EnsureInnerDocument()`, `Instances.ActiveCanvas.Document` IS the inner document — so a follow-up
script that looks for the `HarnessComponent` on "the host canvas" finds nothing and dies on `[0]`.
Detect which document you are on by its contents rather than assuming.

**A harness emitted by `ComponentServer.EmitObject` comes up EMPTY** — no Chat at (0,0). That note
above applies to "Place empty harness" from the chat window, not to a scripted one; a scripted build
must add its own Chat, or the preset loader will refuse the result.

**Picker option values carry the file extension** — `"Rhino Scripting.txt"`, not `"Rhino
Scripting"`. Read `MenuValues` (also internal, also reflection) rather than guessing the spelling.
And an input that should stay empty needs a **stored blank Panel**, NOT a deleted Picker: System
Prompt auto-places one on `Schema`, and left alone it snaps to `values[0]` and folds another
pipeline's JSON schema into the prompt — but deleting it does not survive a load. See the Picker
trap section below, which corrects this.

**Router variable outputs**, one per tool, inserted BEFORE the trailing Feedback output:

```python
idx = router.Params.Output.Count - 1
p = router.CreateParameter(Grasshopper.Kernel.GH_ParameterSide.Output, idx)
router.Params.RegisterOutputParam(p, idx)
router.Params.OnParametersChanged(); router.VariableParameterMaintenance()
```

**Writing a preset without a dialog.** `HarnessComponent.SaveAsPreset()` shows an edit box and would
block an MCP call forever. Write the archive directly instead — and refuse if there is no Chat, since
the loader will:

```python
arch = GH_IO.Serialization.GH_Archive(); arch.AppendObject(inner, "Definition")
arch.WriteToFile(path, True, False)
```

Reading one back works with `GH_Archive.ReadFromFile` + `ExtractObject(GH_Document(), "Definition")`
as well as the `GH_DocumentIO.Open` above; the archive route never touches the document server.

**Beware what a component does to shared Rhino state while you script through the MCP.** Arming
Watch Modelling made every `run_python` call return blank — see [[watch-modelling]]. If MCP stdout
goes silent mid-session, suspect a Physalia component you just switched on, not the MCP server.

## 2026-09-06, later — two more from a session of scripted rigs

**Write the persistent-data helper so it ALWAYS wraps.** The `IEnumerable<char>` trap above was
already written down, and it still landed a second time the same evening: an earlier helper took a
`text=True` flag, a fresh one written later did not, and a Signal Throttle test came back reporting
payloads of `"b"` — the first character of `"burst-1"`. The relay's sequence numbers and counts were
right, so the test *looked* like it had passed. A helper with an opt-in for the safe behaviour is a
helper that will be called wrongly; make wrapping unconditional.

**Rhino crashed mid-`run_python` once**, after a long session of arming and disarming watchers. It
reproduced nothing — every component emits and creates attributes cleanly when probed one at a time —
so it is not attributable. Two things follow: probe a batch by **writing progress to a file** rather
than relying on stdout, which dies with the process; and a fresh Rhino RELOADS the `.gha`, which is
the only way to deploy a build while Rhino has been holding the old one.

**A rig does not need a harness.** The relay and For Each tests ran on a bare canvas with Construct
Signal and Deconstruct Signal, no Chat and no Conversation Log — deterministic, instant and free.
Only delegation needs harnesses, because a Delegate grip-links to one and Task In/Out live inside
one. Reach for the bare-canvas rig first.


## 2026-09-07 — driving GH documents themselves, not just harness contents

From a session of pre-ship rigs ([[pre-ship-testing-pass]]). These are about the DOCUMENT layer,
which the notes above skip because earlier sessions always had a canvas already open.

- **A freshly started Grasshopper has NO document.** `Instances.ActiveCanvas.Document` is null and
  `DocumentServer.DocumentCount` is 0. `AddNewDocument()` is the one that works — `AddDocument`
  takes a *path*, not a `GH_Document`, and `GH_Document.DisplayName` is read-only.
- **`ActiveCanvas.Document` is read-only to Python, but `canvas.set_Document(doc)` works** — the
  property setter reached as a method. That is also how you script "Edit Harness": point the canvas
  at `harness.InnerDocument` and back.
- **Compare documents by `DocumentID`, never with `is`.** Python.NET hands out different proxy
  objects for one .NET document, so `canvas.Document is host` is False while they are the same
  document. This reads as a real failure and sent me chasing a non-existent bug.
- **`GH_DocumentIO.Copy` / `Paste` return True and do nothing** from a script, for every
  `GH_ClipboardType`. There is no canvas/undo context, so GH's paste path cannot be exercised
  headlessly — which means the copy/paste rig has no scripted form.
- **Scripted preset placement, which DOES work:** `HarnessComponent.ReadDocumentFile(path)` (fresh
  ids, host targets cleared) → `HarnessComponent.CreateWith(doc)` → `AddObject`. Both are static and
  reachable by reflection. `DelegateTool.LinkTo(Guid)` is public, so a delegation preset can be
  built end to end.
- **A `Panel`'s `UserText` is only the user-TYPED field.** A panel fed by a wire still reports the
  placeholder there; read `panel.VolatileData` for what actually arrived. I briefly recorded "the
  panel never updates" off the wrong property.
- **`RhinoDoc.Objects.Count` keeps counting deleted objects** held for undo, so it still read 501
  after a successful purge while the table enumerated empty. Iterate the table, or ask the MCP's
  `get_context`, to know what is really there.
- **PowerShell variable names are case-INSENSITIVE**, so `$S` (a source directory) and `$s` (a loop
  variable) are one variable. The symptom is a nonsense path like
  `...\System.Collections.Hashtable\sheet_a.png`, not an error about an unset variable.

## 2026-09-07 — the Picker trap, which applies to every scripted preset

**An input left unwired in the FILE grows a fresh Picker every time the preset is LOADED**, not just
when the component is placed. `AddedToDocument` places and wires a Picker for any input with
`SourceCount == 0`, and deserialization adds objects BEFORE restoring wires — so a file storing 16
objects and no Pickers loads as 19 with 3. Deleting them at build time does not help, and
`ObjectCount` makes it look as though it did. Store a real source on every such input instead: a
Picker where a choice is wanted (set the internal `SetSelectedValue` by reflection — no solve
needed, `Write` serializes the field), a blank Panel where none is. Full account, including why the
Schema case is actively harmful rather than untidy, in [[blender-mcp-preset]].

Also measured that session: **`RhinoCode.exe -r <id> script <file>` returns success and runs
NOTHING** against a live instance. The working channel is `-RunPythonScript` via SendKeys with the
Grasshopper window MINIMIZED first, since GH steals the keystrokes — which is why the channel
appears to work once and then silently stops. And the archive route's freedom from dialogs is now
confirmed: `GH_DocumentIO.Open` pops a missing-plug-in prompt plus a per-object **Grasshopper Font
Mapper** that ignores `{ENTER}`, while `GH_Archive` + `ExtractObject` raised none.

## 2026-09-07 — resolving components by NAME needs the ribbon section too

Scanning `ComponentServer.ObjectProxies` by `proxy.Desc.Name` beats a hard-coded guid table (which
goes stale), but name alone is ambiguous: **four Physalia names are claimed by two components each.**
`Read PDF` is both the `read_pdf` LLM tool and the PDF-intake human tool — and the human-tool half
has NO parameters, so a build that grabs it dies on `no input Signal on Read PDF`. `Component
Catalog`, `Model API` and `Token Estimator` each also collide with a hidden `Params` proxy of the
same name (hidden from the ribbon, present in `ObjectProxies`). Match `Desc.SubCategory` as well, and
check the emitted object has the inputs you are about to wire. Full table in
[[harness-builder-preset]], which also confirms the entire build recipe runs unchanged in
`run_rhino_script`'s CPython 3.9 — a different engine from the IronPython that `-RunPythonScript`
gives you.
