---
name: gh-code-editor-abandoned
description: Why opening the native Grasshopper script editor from a Physalia component was abandoned — Eto version conflict plus a lifecycle requirement GH imposes
metadata: 
  node_type: memory
  type: reference
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:04:12.526Z
---

Investigation concluded and **abandoned**. Goal was to open the native GH Script editor from a PyReceiver double-click.

- `RhinoCodeEditor.dll` uses Eto 2.11.x; the Grasshopper NuGet ships Eto 2.7.x → **CS1705 hard error at compile time**. (This is why every later Eto reference in the repo HintPaths Rhino's own shipped `Eto.dll` instead — see [[resources-tab-image-gatherer]].)
- `Open(3-param)` + `AddCode(Uri)` does work, but opens the **Rhino** editor, not the GH editor with its inputs/outputs dashboard.
- Root cause of that: the GH dashboard requires a persistent `Grasshopper1Script` registered over the component's full lifecycle — not achievable per double-click.

**Decision:** use a custom `ScriptEditorDialog` (Eto.Forms) instead. Don't reopen this without new information from the RhinoCode side.
