---
name: collapsible-harness
description: "The harness is a REAL owned GH sub-document opened on a secondary canvas (rewritten 2026-08-08); the old in-place visual collapse is deleted"
metadata:
  node_type: memory
  type: project
  originSessionId: 3d4edd5b-d992-413c-af74-15bf81d67005
  modified: 2026-08-08T07:53:19.529Z
---

**Rewritten 2026-08-08.** A harness is now a real `GH_Document` owned by a proxy node; double-clicking
the proxy takes the canvas into it, cluster-style. Builds clean; **live-Rhino test pending** (the spike
below is the gate). Related: [[chat-widget]], [[chat-window]], [[preset-placement]], [[chatbox-switcher-row]].

**This inverts the earlier decision.** Until 2026-08-07 the harness was an *illusion*: members stayed in
the main document and `CollapseGuard` / `CollapsedProxyAttributes` / `HarnessParamAttributes` shrank them
to the proxy pivot and skipped `Render`. That leaked endlessly (relays regenerating attributes, live grips
at the collapse point, stale saved pivots, members left behind on drag). All of it is **deleted**:
`Harness.cs`, `CollapseGuard.cs`, `HarnessMembershipUndoAction.cs`, `IHarnessArrow.cs`,
`CollapsedProxyAttributes.cs`, `HarnessParamAttributes.cs`, `PhyBase.HarnessCollapsed/HarnessCollapsePoint`,
the per-attrib guards, the Chat's `Group` + harness menu, and the chat UI's `togglecollapse` verb +
`collapsed`/`harnessCount` state. [[gh-collapsed-harness-grips]] is superseded.

**Why it works here and needs no cluster hooks.** `Chat` has zero inputs and one output, and a Physalia
pipeline never exchanges *dataflow* with the user's canvas — it only *scans* it (grounders, guardrails) and
*writes to it by side effect* (placement). So the whole pipeline, Chat included, moves inside and nothing
crosses as a wire. A normal cluster would need `GH_ClusterInputHook`/`OutputHook`; this does not.

New files in `src/Physalia.GH/`:
- `Harness/HarnessComponent.cs` — `HarnessComponent : PhyBase, IGH_DocumentOwner`, no params. Class name is
  `HarnessComponent` (not `Harness`) because the namespace is `Physalia.GH.Harness`; GH display name is "Harness".
  `OpenInCanvas` / `ReturnToHost` / `CreateFromSelection` / `EnsureInnerDocument` / `InnerDocument` / `Count`.
- `Harness/PhyDocuments.cs` — the document resolver. `Host(doc)` / `Host(obj)` / `ActiveHost()` climb
  `doc.Owner.OwnerDocument()`; `ObjectsIncludingHarnesses(doc)` walks a doc *plus* the harnesses in it;
  `IsHarnessDocument(doc)`.
- `Attributes/HarnessAttrib.cs` — proxy capsule (the old harness tint + pink glow moved here);
  `RespondToMouseDoubleClick` → `OpenInCanvas`.
- `Widgets/HarnessReturnWidget.cs` — the back pill, registered in `ChatWidget.cs`'s `AddWidgets`.

**The local-vs-host split is the whole refactor.** `OnPingDocument()` inside a harness returns the
*sub*-document. Local (unchanged): `ScheduleSolution`/`NewSolution`, `AddedToDocument`/`RemovedFromDocument`,
Feedback→FeedbackCollector guid resolution, ToolsInUse's peer scan, `PromptPipelineView` wire walks — every
endpoint of those moves inside together. Host (`PhyDocuments.Host(this)`): CanvasStateGrounder,
ComponentTransmitter, GeometryReport, RuntimeHealthCheck, FidelityCheck, GeometryObservation,
RhinoGeometryTool, MemoryTool, ConversationLog's canvas export, Chat's generated-geometry bounds. The 13
`Instances.ActiveCanvas?.Document` sites in `GhJsonBridge*` became `PhyDocuments.ActiveHost()` — **those were
the dangerous ones**: while you are editing *inside* a harness the active canvas document IS the pipeline, so
placement and grounding would have targeted the pipeline itself. `GhJsonBridge`'s `??=` fallbacks now
host-resolve whatever they are handed, so a missed caller still behaves.

**The harness deliberately does NOT set `GH_Document.Owner`, and does NOT register with the
DocumentServer.** `HarnessComponent.Owners` (a `ConditionalWeakTable<GH_Document, HarnessComponent>`)
maps inner doc → proxy instead, and `PhyDocuments.Host` follows it, falling back to `Owner` for real
GH clusters. Reasons, all verified by decompiling `GH_Canvas`:
- `Owner != null` makes `GH_Canvas` paint its **own hard-coded cluster icon** top-left
  (`_regionCluster`, `ClientRectangle.X+5, Y+25`, `Res_GUI.OpenCluster_Empty_63x56`). It is drawn
  directly in the canvas paint and hit-tested in `GH_Canvas_MouseDown` via `IsCursorOnClusterIcon()`
  — **not a widget**, so it cannot be removed from the widget list or reliably painted over. Its
  menu runs "Save and Return" → `RemoveDocument` → **Dispose**. Not setting Owner removes it, leaving
  `HarnessReturnWidget` as the only affordance.
- Bonus: `Owner`'s setter does `m_renderQueue.Enabled = (value == null)`, so previews from inside a
  harness now work.
- Skipping `AddDocument` is safe: the canvas setter only calls `PromoteDocument`, which no-ops for an
  unregistered document (`IndexOf` returns -1). Staying out of the server keeps the harness off the
  document dropdown, out of the exit-time save sweep, and out of reach of `RemoveDocument`.
- Residual: File > Save while inside a harness saves the *inner* doc (Save As dialog, stray file).
  Non-destructive, but the return widget is the intended exit.

**Gestures (2026-08-08, second pass):** double-click the proxy → opens the **chat window** on the Chat
inside (`HarnessComponent.FindChat`); right-click → **"Edit Harness"** enters the document. The proxy
stands in for the Chat that moved into it, so it answers the Chat's gesture. Chat menu item is
**"Add to Harness"**.

**Arrows moved to the proxy.** `PyTransmitterAttrib` and `CompTxAttrib` are **deleted**; both
transmitters now use plain `PhyComponentAttributes` (no grip, no arrow). `IHarnessArrow` is
reinstated in `Harness/` but implemented by the **components** (`PyTransmitter`,
`ComponentTransmitter`), not their attributes, and `HarnessAttrib : ArrowAttributeBase` hosts the
grip + drag and forwards the drop — showing the arrow only when the harness holds exactly one
implementer (`TryGetSoleArrow`). This is required, not cosmetic: the arrow's targets (a script
component, a placement point) live on the host canvas and a drag cannot cross two canvases.
`ComponentTransmitter`'s placement offset is now measured from the **proxy's** pivot
(`ArrowAnchor`), not its own — the transmitter is in a different coordinate space from the drop point.

Decompiled-GH facts that shaped it (from `Grasshopper.dll` 8.x + `Grasshopper.xml`):
- `GH_Canvas.Document` is a **settable** property and the ONLY in-memory way to show a document.
  `GH_DocumentEditor` has no `SetDocument`/`LoadDocument`.
- Its setter sets the **outgoing** doc `Enabled = false` — so `ReturnToHost` re-enables the inner doc, and
  `SolveInstance` re-asserts it every solve. The pipeline must keep solving while off-canvas.
- `GH_DocumentServer.RemoveDocument` **disposes** the document → never call it on the live inner doc. It is
  registered once per session and left registered.
- GH's `GH_Cluster.EditClusterAsSeparateDocument` edits a **duplicate** and merges back via
  `DocumentModified`. Physalia can't: signals/conversation/solve state are session-only and non-persisted, so
  a `Write`/`Read` round-trip would wipe the live chat. **We edit the live document.**
- **There is no breadcrumb or back button in GH — not for clusters either.** Only a relabelled File entry
  ("Save and Return"), and that path is destructive (RemoveDocument → Dispose). Hence the return widget.
- Persistence: `writer.CreateChunk("HarnessDocument")` + `GH_Document.Write/Read`. This is the plug-in's only
  nested-archive persistence; everything else writes flat keys + guid refs.

Entry points: Chat menu "Move Selected into a Harness" → `HarnessComponent.CreateFromSelection` (archive
round-trip: `host.Write(chunk, moving)` → `inner.Read(chunk)` → remove originals → drop proxy at the
selection centroid, one `GH_UndoRecord`). **The move replaces every instance**, so the Chat the window was
bound to is deleted and a copy lands inside — both `Chat.MoveSelectionIntoHarness` and
`ChatWindow.MoveIntoHarness` re-`SetActiveComponent` to the relocated Chat. Presets now land in a harness the
same way. `ChatWidget.FindChat`, `ChatWindow.EnumerateChats` and `HandleClearAll` walk
`ObjectsIncludingHarnesses`. `RuntimeMessageTrace` now hooks `SolutionEnd` on the host **and every harness
inner doc** (it scans *for* pipeline components, so it follows them in) and re-syncs its watch set each solve.

`PyTransmitter` gained a **"Link to Script Component" menu picker** listing the host canvas's script
components: a grip drag cannot cross two canvases (`GripLinkAttrib.OnDrop` hit-tests the drawn document's
objects). Its `ResolveTarget` and `InterfaceLock`'s script lookup now resolve against the host; the
transmitter↔InterfaceLock link stays local (both are Physalia nodes inside the harness).

**UNVERIFIED — the spike that gates everything:** does an owned, registered, `Enabled = true` document run its
own `ScheduleSolution` timer while it is NOT the canvas document? The entire signal lifecycle is scheduler-
driven. If it does not, the fallback is the cluster model and it is small: route
`StatefulComponentBase.ScheduleAt` (the single funnel) to schedule on `PhyDocuments.Host(this)` and have
`HarnessComponent.SolveInstance` forward with `_inner.NewSolution(false)`.

Plan file: `C:\Users\rober\.claude\plans\let-s-try-something-different-glimmering-sunbeam.md`.
