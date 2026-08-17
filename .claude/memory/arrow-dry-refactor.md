---
name: arrow-dry-refactor
description: Drag-arrow rendering unified into a shared ArrowGrip controller + pluggable arrow heads + central gradient palette; CollapsedProxyAttributes moved to Attributes/
metadata: 
  node_type: memory
  type: project
  originSessionId: ece7868a-c4be-4a7f-985e-b67c2bce2933
  modified: 2026-08-17T04:11:48.592Z
---

2026-06-25 refactor (builds clean `-c Debug`, **live Rhino test pending**).

**Move:** `CollapsedProxyAttributes.cs` moved `Harness/` → `Attributes/` (namespace `Physalia.GH.Attributes`); it was the only attribute class living under `Harness/`. `CollapseGuard.cs` gained `using Physalia.GH.Attributes;` for its `<see cref>`; `Harness.cs` already imported the namespace.

**Arrow DRY (all in `Attributes/UiElements/`):**
- The triplicated grip + wire-cache + drag state machine (was duplicated across `GripLinkAttrib`, `CompTxAttrib`, `ChatAttrib`) now lives in one **`ArrowGrip`** controller, driven by an **`IArrowHost`** interface (`ArrowOrigin`, `ArrowGradient`, `ArrowHead`, `HorizontalArrow`, `SettledEndpoints(doc)`, `OnDrop(doc,pt,ctrl)`). Used by **composition** because `ChatAttrib` can't change its `GH_ComponentAttributes` base. Each host keeps only its own hit-test in `RespondToMouseDown` then calls `_arrow.StartDrag/UpdateDrag/EndDrag`; `IsDragging` gates Move/Up.
- `ChatAttrib`'s old `_wires`/`WireAt`/`DrawArrowWires` are gone (folded into `ArrowGrip`); its harness tint/glow (`RenderSmoothCapsule`/`DrawHarnessGlow`/`LinearGradientBrush`) is untouched — not arrow code.
- **Two bugs fixed 2026-08-10, both in the shared layer, so every grip component was affected:**
  - **`TryStartDrag` tested `GripBounds`** — the whole node — so a press ANYWHERE on a Feedback /
    ScriptIO (was InterfaceLock) / ZoomGuid / PyTransmitter / harness pulled out a wire and the component could not be
    dragged around the canvas at all. Now tests `GripHitRegion`: a `GripExpansion`-radius square on
    `GripOrigin`. Keep the two distinct — `GripBounds` is the PICK region and must stay node-sized or GH
    won't route the mouse-down to the component in the first place.
  - **`BezierWire` always departed DOWNWARD** (`cp1 = start + (0, offset)`), written when every grip was
    bottom-centre. Now `HorizontalStart` mirrors `HorizontalEnd`, and `IArrowHost.HorizontalArrow`
    governs the departure — a right-edge grip must set off rightwards, not dive under its own node first.
    Only `HarnessAttrib` sets it true, so no other wire changed shape.
- **2026-08-16: the two ends were SPLIT.** `IArrowHost` gained `HorizontalArrowEnd` (arrival) beside
  `HorizontalArrow` (departure); `ArrowAttributeBase` defaults it to the departure so single-arrow
  components stay one override. `IHarnessOutlet` carries the same member — the outlet owns its endpoints,
  so it owns how the wire lands, while the departure stays the proxy's (always rightwards, right-edge grips).
  **Script transmitters (Py, C#) override it false**: they feed no input, they REWRITE a component, so
  the wire turns up under the target and points at it. TextTx/CompTx keep true (they end where data goes).
  `TransmitterLink`'s tip drop is now `TriangleArrowHead.Default.Height`, not a number: the head is drawn
  FORWARD of the wire end (the end is the triangle's base centre), so the drop must EQUAL the head height
  for the tip to meet the node's bottom edge — 6f bit into the capsule, 15f left a visible gap under it.
  **Same day, follow-up — the mixed case needed a new CURVE, not just a new tip.** With the fixed 80f
  control offsets a turning wire SAGGED: `cp2 = end + (0,80)` sits below the grip whenever the target is
  under 80 above it, so the wire dived before it climbed. `BezierWire.ControlPoints()` now splits the two
  cases — ends that AGREE keep the fixed push (the usual slack S), ends that DISAGREE put BOTH control
  points on the ELBOW (level with the start, plumb with the end), which gives a flat run out of the grip,
  one turn, and a vertical rise into the target, scaling with the gap by itself. Clamped by `_elbowMinimum`
  (30f) so a target to the left still departs rightwards and one below still arrives from underneath.
- `GripLinkAttrib` stays as the thin base for the three link attributes; its abstract surface is unchanged, so `FeedbackAttrib`/`PyTransmitterAttrib`/`ZoomGuidAttrib` only changed their gradient line.
- **Pluggable heads:** new `IArrowHead` + `TriangleArrowHead` (default, current 8f/4f geometry). `BezierWire` gained an `ArrowHead` property and derives tip orientation from its own end tangent (the old `horizontal` bool inside `DrawArrow` is gone; `HorizontalEnd` still shapes the *curve*).
- **Central gradients:** new `ArrowStyles` static class holds every wire `WireGradient` (`Feedback`, `PyTransmitter`, `CompTx`, `ZoomGuid`, `Proxy`); per-class gradient constants removed.

**Attribute inheritance spine (follow-up, same day):** the collapse-guard + bottom-grip boilerplate (was duplicated across GripLink/CompTx/FeedbackCollector) is now a base-class spine:
`GH_ComponentAttributes → PhyComponentAttributes (collapse guard; exposes `protected IsHarnessCollapsed`; the ONLY attribute that references `CollapseGuard.IsCollapsed/TryCollapseLayout`) → BottomGripAttributes (10px downward grip expansion, `_visualBounds`/`GripBounds`/`BottomCentre`, owns the `CanvasGrip` + draws it, `RenderGripContent` hook) → ArrowAttributeBase : IArrowHost (owns `ArrowGrip`, wire render + Move/Up + virtual `TryStartDrag`) → {GripLinkAttrib → Feedback/Py/Zoom, CompTxAttrib}`.
- `FeedbackCollectorAttrib` is now ~10 lines (just `: BottomGripAttributes`). `PickerAttrib` and `ChatAttrib` rebased on `PhyComponentAttributes` (use `IsHarnessCollapsed`, no direct CollapseGuard). `ChatAttrib` stays a bespoke proxy (own `CanvasGrip`, composition `ArrowGrip`).
- **`ArrowGrip` no longer owns/draws the grip dot** — it's wires + drag only; the grip is drawn by `BottomGripAttributes` (or Chat itself). Grip-draw helper pattern: subclass override `DrawGrip`/`RenderGripContent`.
- Leaf gradient member renamed `Gradient` → public `ArrowGradient` (the `IArrowHost` member); GripLinkAttrib dropped the indirection.
- `CollapseGuard.CollapseParams` is still called by `CollapsedProxyAttributes` (native-node param collapse — different API, legit, it's a `GH_Attributes<IGH_DocumentObject>` not in the spine).
- `PrompterAttrib` left untouched (bespoke; note it does NOT honour CollapseGuard — possible real gap, not addressed).

Note: the arrow is now hosted by the **harness proxy**, not the transmitters — see [[harness-subdocument]] — but it still routes through `ArrowGrip` + this spine.
