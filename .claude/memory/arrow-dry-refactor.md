---
name: arrow-dry-refactor
description: Drag-arrow rendering unified into a shared ArrowGrip controller + pluggable arrow heads + central gradient palette; CollapsedProxyAttributes moved to Attributes/
metadata: 
  node_type: memory
  type: project
  originSessionId: ece7868a-c4be-4a7f-985e-b67c2bce2933
---

2026-06-25 refactor (builds clean `-c Debug`, **live Rhino test pending**).

**Move:** `CollapsedProxyAttributes.cs` moved `Harness/` → `Attributes/` (namespace `Physalia.GH.Attributes`); it was the only attribute class living under `Harness/`. `CollapseGuard.cs` gained `using Physalia.GH.Attributes;` for its `<see cref>`; `Harness.cs` already imported the namespace.

**Arrow DRY (all in `Attributes/UiElements/`):**
- The triplicated grip + wire-cache + drag state machine (was duplicated across `GripLinkAttrib`, `CompTxAttrib`, `ChatAttrib`) now lives in one **`ArrowGrip`** controller, driven by an **`IArrowHost`** interface (`ArrowOrigin`, `ArrowGradient`, `ArrowHead`, `HorizontalArrow`, `SettledEndpoints(doc)`, `OnDrop(doc,pt,ctrl)`). Used by **composition** because `ChatAttrib` can't change its `GH_ComponentAttributes` base. Each host keeps only its own hit-test in `RespondToMouseDown` then calls `_arrow.StartDrag/UpdateDrag/EndDrag`; `IsDragging` gates Move/Up.
- `ChatAttrib`'s old `_wires`/`WireAt`/`DrawArrowWires` are gone (folded into `ArrowGrip`); its harness tint/glow (`RenderSmoothCapsule`/`DrawHarnessGlow`/`LinearGradientBrush`) is untouched — not arrow code.
- **Two bugs fixed 2026-08-10, both in the shared layer, so every grip component was affected:**
  - **`TryStartDrag` tested `GripBounds`** — the whole node — so a press ANYWHERE on a Feedback /
    InterfaceLock / ZoomGuid / PyTransmitter / harness pulled out a wire and the component could not be
    dragged around the canvas at all. Now tests `GripHitRegion`: a `GripExpansion`-radius square on
    `GripOrigin`. Keep the two distinct — `GripBounds` is the PICK region and must stay node-sized or GH
    won't route the mouse-down to the component in the first place.
  - **`BezierWire` always departed DOWNWARD** (`cp1 = start + (0, offset)`), written when every grip was
    bottom-centre. Now `HorizontalStart` mirrors `HorizontalEnd`, and `IArrowHost.HorizontalArrow`
    governs BOTH ends — a right-edge grip must set off rightwards, not dive under its own node first.
    Only `HarnessAttrib` sets it true, so no other wire changed shape.
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
