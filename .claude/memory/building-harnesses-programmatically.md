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

## 2026-09-07 — twenty-eight of them, and the toolkit that made it repeatable

Hand-writing the same `EmitObject` / `CreateAttributes` / `AddObject` dance per preset does not
scale past about three. `tools/presets/phybuild.py` wraps all of it; **read
[[preset-build-runbook]] before building anything** — it is the operational procedure and the
helper reference. What follows is only the GH-level knowledge that was added.

### `core_loop()` — the six components every pipeline repeats

Chat + System Prompt + Conversation Log + a Model + LLM Call + the reply Feedback path, in one call,
returning a dict of the pieces. Written after the third scenario preset, because hand-wiring that
spine ten more times is how a Feedback ends up pointing at nothing. It also takes an `instruction=`
string and hangs it off System Prompt's **`Additional Prompt`** as a white `input_panel`, which is
where a preset's per-job wording belongs (it ships in the file, unlike anything typed in the chat).

### Router slot counting is off by one, and getting it wrong is SILENT

**`router_slots(router, n)` adds n slots to the default one**, so it yields `n + 1` tool outputs, and
the LAST Router output is always **Feedback**. Wiring a tool's `Signal` to an index past the last
tool slot therefore lands it on the feedback path: the tool is never dispatched AND never advertised,
so the model is told it does not exist. Nothing errors, no sweep sees it, the canvas looks right.
Hit on S10 with Pipeline State. `check_pairs.py` now walks every Router's last output across every
preset.

### Topologies that are legal and were not obvious

- **TWO Conversation Logs in one harness.** A pipeline normally has one, but nothing enforces that.
  Writer and critic, joined by ONE wire: the writer's `Success Signal` into the critic's
  `Prompt Signal`. A signal carries its text, so an answer simply becomes the next question. Each
  half keeps its own System Prompt and its own history — which is the point, since the critic must
  not see the writer's reasoning. Only one Chat is needed (the loader requires ≥1).
- **Nested harnesses inside a preset.** `place(D, "Harness", ...)` then `EnsureInnerDocument()`, and
  `delegate.LinkTo(harness.InstanceGuid)`. Verified that both grip links AND both inner documents
  survive the loader's id reissue.
- **A VALUE rather than prose on a Harness Out** (Pipeline State's `Value`). That is what separates
  a tool from a chat about the same subject.
- **Pre-linking a grip link inside a preset works** and is worth doing: `scriptio.LinkTo(cstx.
  InstanceGuid)` leaves the user only the ONE link that must be made on their own canvas.

### Component API corrections found by building against the real components

CLAUDE.md is wrong or stale on several of these — **trust `pin()`'s error message, which lists the
real parameter names.**

| Component | Reality |
|---|---|
| Read PDF (LLM tool) | input is **`Reference Folder`**, not `PDF Folder`, and it means the SHARED office library, not a per-pipeline folder |
| Folder Watcher | `Project Folder / Filter / Subfolders / Settle` — there is **no `Instruction` input** |
| Signal Limiter | takes **`Count`**; emits **`Within Limit` / `Over Limit`** |
| LLM Call | has **no `Response` output** — the reply text rides the signal, so read it with a Deconstruct Signal |
| C# Transmitter | publishes **no code output** — the code is in the target component (`GhPythonBridge.GetScript` reads it back) |
| Construct Signal | the button input is **`Trigger`**, not "Boolean Trigger" |
| For Each | `Items / Start / Next / Reset` → `Item Signal / Done Signal / Item / Index` |
| Take Snapshot | `Current Location` is a **Point** — a `blank_input` text panel into it is a conversion error |
| Web tools | the node is **`Read URL`**, not "Read Url" |
| `place()` | `sub=` (ribbon section) is REQUIRED where names collide — `"Read PDF"` is both an LLM tool and a Human tool |

### Two script-component facts, confirmed live

`IsLinkTarget` accepts the Rhino 8 **`CSharpComponent`** (`b6ba1144-02d6-4a2d-b53c-ec62e290eeb7`)
and REFUSES the obsolete **`Component_CSNET_Script`** (`a9a8ebd2-fff5-4c44-a8f5-739736d129ba`). Both
are called "C# Script" and both sit under Maths / Script, so the `LanguageSpec` test is the only
thing separating them. And **Set Script I/O reads its target THROUGH the transmitter's link**, so it
never needs one of its own.
