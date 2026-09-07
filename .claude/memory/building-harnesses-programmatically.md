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
And **delete a Picker whose input should stay empty**: System Prompt auto-places one on `Schema`, and
left alone it snaps to `values[0]` and folds another pipeline's JSON schema into the prompt.

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
