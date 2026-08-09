---
name: harness-subdocument
description: "The Harness is a real owned GH sub-document and the plug-in's base unit — every Physalia component lives inside one"
metadata: 
  node_type: memory
  type: project
  originSessionId: 8ba6eaa2-c1c1-4bb2-9487-ac5379251e1f
  modified: 2026-08-09T06:19:21.849Z
---

Built 2026-08-08 (replaces the in-place "collapsible harness" entirely). Builds clean; **no part of
this has been run in Rhino yet.** Related: [[chat-window]], [[chat-widget]], [[chatbox-switcher-row]],
[[ghjson-library-reference-only]].

## What it is

A **Harness** is a `HarnessComponent` (`src/Physalia.GH/Harness/`) holding its own `GH_Document`. The
user's canvas carries only the proxy node; the whole Physalia pipeline — Chat included — lives inside.
Right-click → **"Edit Harness"** points the canvas at the inner document; **double-click opens the chat
window** on the Chat inside (the proxy stands in for that Chat, so it answers the Chat's gesture).

**No cluster input/output hooks are needed.** `Chat` has zero inputs and one output, and a Physalia
pipeline never exchanges *dataflow* with the user's canvas — it only *scans* it (grounders, guardrails)
and *writes to it by side effect* (placement). So nothing crosses the boundary as a wire.

Files: `HarnessComponent.cs`, `PhyDocuments.cs`, `HarnessResidency.cs`, `IHarnessArrow.cs`,
`Attributes/HarnessAttrib.cs`, `Widgets/HarnessReturnWidget.cs`.

## The local-vs-host document split — the core of it

`OnPingDocument()` inside a harness returns the **sub**-document. `PhyDocuments` resolves the rest:
`Host(doc)` / `Host(obj)` / `ActiveHost()` climb to the root; `ObjectsIncludingHarnesses(doc)` walks a
document plus the harnesses in it; `IsHarnessDocument(doc)`.

- **Stays local:** `ScheduleSolution`/`NewSolution`, `AddedToDocument`/`RemovedFromDocument`,
  Feedback→FeedbackCollector guid resolution, ToolsInUse's peer scan, `PromptPipelineView` wire walks.
  Every endpoint of those moves into the harness together.
- **Becomes `PhyDocuments.Host(this)`:** CanvasStateGrounder, ComponentTransmitter, GeometryReport,
  RuntimeHealthCheck, FidelityCheck, GeometryObservation, RhinoGeometryTool, MemoryTool,
  ConversationLog's canvas export, Chat's generated-geometry bounds, InterfaceLock's *script* lookup
  (its PyTransmitter lookup stays local).
- **Becomes `PhyDocuments.ActiveHost()`:** the 13 `Instances.ActiveCanvas?.Document` sites in
  `GhJsonBridge*`. **The dangerous ones** — while the user edits *inside* a harness the active canvas
  document IS the pipeline. `GhJsonBridge`'s `??=` fallbacks host-resolve whatever they are handed, so
  a missed caller still behaves.
- `RuntimeMessageTrace` goes the OTHER way: it scans *for* pipeline components, so it hooks
  `SolutionEnd` on the host **and every harness inner doc**, re-syncing the watch set each solve.

## Ownership: deliberately NOT GH_Document.Owner

`HarnessComponent.Owners` is a `ConditionalWeakTable<GH_Document, HarnessComponent>`; `PhyDocuments.Host`
follows it and falls back to `doc.Owner?.OwnerDocument()` so real GH clusters still resolve. Reasons,
verified by decompiling `GH_Canvas`:

- `Owner != null` makes GH paint its **own hard-coded cluster icon** top-left (`_regionCluster`,
  `Res_GUI.OpenCluster_Empty_63x56`), drawn directly in the canvas paint pass and hit-tested in
  `GH_Canvas_MouseDown` ahead of widgets — **not a widget**, so it cannot be removed from the widget
  list or reliably painted over. Its menu runs "Save and Return" → `RemoveDocument` → **Dispose**.
- Bonus: `Owner`'s setter does `m_renderQueue.Enabled = (value == null)`, so leaving it null keeps
  Rhino previews working from inside a harness.
- The inner doc is also **not registered with the DocumentServer**: the canvas setter only calls
  `PromoteDocument`, which no-ops for an unregistered document. Staying out keeps the harness off the
  document dropdown, out of the exit-time save sweep, and out of reach of `RemoveDocument`.
- Residual: File > Save while inside a harness saves the *inner* doc (Save As dialog, stray file).
  Non-destructive; the return widget is the intended exit.

Other decompiled facts that shaped it: `GH_Canvas.Document` is settable and the ONLY in-memory way to
show a document (`GH_DocumentEditor` has no `SetDocument`); its setter disables the **outgoing**
document, so `ReturnToHost` re-enables the inner one and `SolveInstance` re-asserts it every solve;
GH's own `GH_Cluster.EditClusterAsSeparateDocument` edits a **duplicate** and merges back, which
Physalia cannot do because signals/conversation/solve state are session-only and non-persisted — **we
edit the live document**; and there is **no breadcrumb or back button in GH**, hence
`HarnessReturnWidget` (registered in `ChatWidget.cs`'s `AddWidgets`).

Persistence: `writer.CreateChunk("HarnessDocument")` + `GH_Document.Write/Read` — the plug-in's only
nested-archive persistence. Saving the host file writes the harness and its contents; saving *while
inside* a harness writes just that harness's contents, which is what makes `.gh` presets authorable.

## The GhJSON library resolves its own document

`CanvasReader.GetActiveDocument()` is literally `Instances.ActiveCanvas.Document`. `PutOptions` has no
document field, `CanvasPlacer` is `internal`, and there is no `Put` overload taking one — so
host-resolution at our call sites does NOT reach it. The library is reference-only, so:

- **Writes** (`Put` ×3, `Delete` ×1) are wrapped in `PhyDocuments.OnHostCanvas(...)`, which points the
  canvas at the host for the call and restores it in a `finally`. GH's canvas setter saves/restores
  each document's viewport target and zoom, so the user's position inside the harness survives.
- **Reads** must NOT swap — `GetByGuids` runs on every canvas-state export and swapping per solve would
  thrash. Replaced by `GhJsonBridge.SerializeByGuids(doc, guids)` over the public
  `GhJsonGrasshopper.Serialize(objects, options)`. Keep document order (it feeds id assignment) and set
  `IncludeSelectedState = false` — the one default where `SerializationOptions` (true) disagrees with
  `GetOptions` (false). This was a **grounding** bug as much as a placement one: inside a harness the
  model was handed an empty canvas.

## Residency: Physalia components must live in a harness

`HarnessResidency`, hooked from `PhyBase.AddedToDocument` (every subclass override already called base).
A `PhyBase` that is not a `HarnessComponent` and lands outside a harness is **removed on the next idle
pass**, reason written to the Rhino command line. Deliberately not undoable — an undo would re-add it
and trip the guard again. Four exemptions:

1. `HarnessComponent` itself.
2. Anything already in a harness document.
3. `GhJsonBridge.IsImporting`.
4. **Anything added to a document that is not the canvas document** — this is how a file load is told
   apart from a user placement (GH reads every object in before handing the document to the canvas).
   *`GH_Document.Context` looks like the natural signal but its own SDK docs say it is a setter, not a
   getter — do not use it for this.*

The idle deferral also re-checks each queued component, so anything swept into a harness meanwhile is
spared. Pre-harness files load untouched; migrate by opening a harness and pasting the pipeline in.

## Arrows live on the proxy

Transmitters draw no arrow (`PyTransmitterAttrib` / `CompTxAttrib` deleted; both use plain
`PhyComponentAttributes`). `IHarnessArrow` is implemented by the **components** (`PyTransmitter`,
`ComponentTransmitter`), and `HarnessAttrib : ArrowAttributeBase` hosts the grip and forwards the drop,
showing it only when the harness holds exactly one implementer (`TryGetSoleArrow`). Required, not
cosmetic: the arrow's targets live on the host canvas and **a drag cannot cross two canvases**.
`ComponentTransmitter`'s placement offset is measured from the **proxy's** pivot (`ArrowAnchor`).
For the same reason `PyTransmitter` gained a **"Link to Script Component" menu picker** over the host
canvas's script components.

## Presets are stock .gh files

`Files/PRESETS/*.gh`, each holding one harness's worth of pipeline. `HandlePlacePreset` reads the
archive (`HarnessComponent.ReadDocumentFile` — `GH_Archive` + chunk `"Definition"` +
`DestroyProxySources`, **not** `GH_DocumentIO.Open`, which stamps FilePath and pollutes GH's
recent-files MRU), wraps it in a NEW harness (`HarnessComponent.CreateWith` → `DropPresetHarness`) and
re-points the window at the Chat inside. Refuses a preset carrying no Chat. The gallery lists file
names only — a `.gh` has no readable description. The whole GhJSON preset splice is deleted:
`LoadAndPlaceAnchored`, `RewireAnchor`, `RewireRequest`, `TryReadMetadataDescription`.

## Placement is user-driven only, and repeatable

`MaybePlaceComponent` is gone — the widget/window place nothing on their own. The connect screen has
exactly two options plus LLM setup: **"Place predefined harness"** (gallery) and **"Place empty
harness"** (`placeemptyharness` → `HandlePlaceEmptyHarness`). "Connect a Conversation Log" and its verb
are deleted. An empty harness holds only a Chat, so `connected` stays false and the connect screen
correctly remains.

**Both options stay on offer for good — a document may hold many harnesses** (2026-08-08; the original
build allowed exactly one). A harness exchanges no dataflow with anything outside itself, so there is
no reason to cap it at one, and the two single-harness mechanisms are gone:

- `HandlePlaceEmptyHarness` no longer early-returns on "already placed", and `HandlePlacePreset` no
  longer swaps an existing harness's contents — `ReplaceInnerDocument` was its only caller and is
  **deleted**. Presets accumulate; nothing running is ever destroyed.
- `DropHarness` mints a **new `Chat`** per harness, consuming the window's own only when it is still
  detached (the widget-created first placement), then `SetActiveComponent`s onto it. Taking the viewed
  Chat would leave the earlier harness with nothing driving it.
- `PlaceHarness` + `MoveTo` + `Overlaps` replace the fixed anchor: lay out, then step down a row at a
  time (`PlacementGap`, capped at `MaxPlacementRows`) until the proxy clears everything on the host.
  `GH_Group`s are skipped as obstacles — they are containers drawn behind their members, so the master
  "Physalia" group would otherwise push every harness far down the canvas.
- `HostForPlacement` resolves through `PhyDocuments.Host`, so placing while *inside* a harness lands on
  the user's canvas rather than nesting.
- The `harnessPlaced` wire field is **gone** from `UiState`/`App.svelte`/`ConnectOptions`; it survives
  only as a Tick local picking the status-line wording. The header menu gained **"Add empty harness"**
  beside "Add preset" — the connect screen is hidden once a conversation starts, so without it the
  empty-harness path would be unreachable after the first one.

`ChatWidget.FindChat`, `ChatWindow.EnumerateChats` and `HandleClearAll` walk `ObjectsIncludingHarnesses`.
`Chat.MoveSelectionIntoHarness` / `HarnessComponent.CreateFromSelection` were removed with the
"Add to Harness" menu item.

## UNVERIFIED — the spike that gates everything

Does an owned, `Enabled = true` document run its own `ScheduleSolution` timer while it is NOT the canvas
document? The entire signal lifecycle is scheduler-driven. If it does not, the fallback is small: route
`StatefulComponentBase.ScheduleAt` (the single funnel) to `PhyDocuments.Host(this)` and have
`HarnessComponent.SolveInstance` forward with `_inner.NewSolution(false)`.
