---
name: gh-collapsed-harness-grips
description: "Blocking wire-drag from a collapsed GH harness requires gating param-attribute grips, not the component"
metadata: 
  node_type: memory
  type: reference
  originSessionId: 810a75de-1e1b-41a1-87ef-d15080b000b0
  modified: 2026-08-08T07:20:31.757Z
---

**SUPERSEDED 2026-08-08** — the in-place collapse this describes is deleted; a harness is now a real
owned sub-document, so there are no hidden members and no grips to gate (`HarnessParamAttributes` and
`CollapsedProxyAttributes` no longer exist). See [[collapsible-harness]]. The **GH internals below are
still accurate** and worth keeping: how wire drags resolve, and why grip flags live on param attributes.

To stop wires being pulled out of a collapsed Physalia harness (the proxy Chat + its hidden members piled at the proxy pivot), you must gate the **parameter attributes'** `HasInputGrip`/`HasOutputGrip`, not the component's.

Key GH facts (decompiled from Grasshopper 8.24):
- Wire-drag starts from `GH_Document.RelevantObjectAtPoint`, which iterates **all** attributes (components AND params) and treats any with `HasInputGrip`/`HasOutputGrip` true and a grip within 12px as a `grip_in`/`grip_out` → `GH_WireInteraction`. (Note: `FindAttributeByGrip`, used only for the hover cursor, filters to `DocObject is IGH_Param` — different method, same flags.)
- `GH_ComponentAttributes.HasInputGrip`/`HasOutputGrip` are already `false`; the real grips live on the params' `GH_LinkedParamAttributes` (`HasInputGrip => Owner.Kind == input`, output likewise).
- Collapsing a harness member shrinks its param bounds to the proxy pivot (`CollapseGuard.CollapseParams`) but the param attributes still report grips there, so you can pull wires off the hidden cluster. Component-render being skipped hides the *wires*, not the *grips*.
- Wire **rendering** reads the grip *position* (`source.Attributes.OutputGrip`) directly and ignores `HasOutputGrip`, so gating the flags off doesn't break external wires converging on the collapsed proxy, nor the data connections (those are param Sources, not grips).

Fix: `Attributes/HarnessParamAttributes.cs` (a `GH_LinkedParamAttributes` subclass) returns no grips while `harness.Collapsed`. Applied to the proxy's output in `Chat.CreateAttributes` (GH only auto-creates linked param attributes when `param.Attributes == null`, so pre-assigning wins), and to every member's params in `Harness.HideMember`/`ShowMember` (stash originals in `_swappedParams`, restore on expand — `Remove`/`ApplyState` route through `ShowMember`). Net: collapsed harness is draggable but non-wireable. See [[collapsible-harness]], [[gh-nopreview-hidden-palette]].

**Relay leak fix (2026-07-01).** A `GH_Relay` member stayed clickable/draggable/hoverable through the closed harness. Cause: a relay recreates its own `GH_RelayAttributes` on solve, discarding the `CollapsedProxyAttributes` hide swap (`_swapped`). A native slider keeps the swap; a relay does not. Three-part fix: (1) `CollapsedProxyAttributes.IsPickRegion(PointF) => false` — a hidden member is never the object under the cursor even if its bounds quirk (a relay's hit region isn't bounds-derived). (2) `Harness.RefreshCollapsePoint` now RE-HIDES any non-`PhyBase` member whose attributes have reverted from `CollapsedProxyAttributes` back to native (calls `HideMember` again); it no longer early-returns when the proxy hasn't moved (only the point-push is gated on `moved`, the revert-check runs every call). (3) `Chat` subscribes `document.SolutionEnd` → `RefreshCollapsePoint`, so the re-hide fires right after the solve that regenerated the relay's attributes. Builds clean; live Rhino test pending. Residual risk: if a relay self-heals its attributes on every getter access (not just per-solve), the swap can't hold and we'd need to exclude relays from membership or bridge their wire instead — verify live.
