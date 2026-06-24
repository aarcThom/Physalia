---
name: collapsible-harness
description: "Collapse/expand a Chatbox's pipeline behind the single Chatbox proxy node (in-place visual collapse); new Harness class + folder"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3d4edd5b-d992-413c-af74-15bf81d67005
---

Collapsible harness feature (landed 2026-06-24, builds clean, **live-Rhino test pending**). A Chatbox can hide/show the pipeline components it owns, collapsing them behind the single Chatbox node (the Chatbox IS the proxy — distinct collapsed look: accent double-outline + member-count badge + a chevron toggle at top-right). Related: [[chat-widget]], [[chat-window]], [[preset-placement]], [[chatbox-switcher-row]].

**Mechanism = in-place visual collapse** (NOT a sub-document cluster, NOT off-canvas teleport — both rejected; cluster route would force every side-effect component to climb `OnPingDocument().Owner` + re-validate the whole signal lifecycle in a sub-doc). Members stay in the main document and keep solving; only rendering changes. GH has no native per-component hide flag (`IGH_PreviewObject.Hidden` = geometry preview only), so it's simulated.

New folder `src/Physalia.GH/Harness/`:
- `Harness.cs` (class `Harness`, namespace `Physalia.GH.Harness`) — owns member `HashSet<Guid>`, collapsed flag, swapped-attrs map; `Add/Remove/SetCollapsed/Toggle/ApplyState/Prune/RefreshCollapsePoint/Write/Read`. Persisted with the Chatbox. NOTE namespace==class name clash, so Chatbox aliases `using HarnessGroup = Physalia.GH.Harness.Harness;` and exposes it as `Chatbox.Group`.
- `CollapseGuard.cs` — static; Physalia (`PhyBase`) members hide via a `HarnessCollapsed`+`HarnessCollapsePoint` flag on PhyBase; their attributes shrink the component + all param grips to a zero-size rect at the shared collapse point (the Chatbox pivot) and skip Render. All members share ONE point so internal wires become zero-length (vanish); only boundary wires to external nodes leave a short stub. Pivot is never mutated → expand restores for free.
- `CollapsedProxyAttributes.cs` — `GH_Attributes<IGH_DocumentObject>` zero-render wrapper for **non-Physalia** members (native sliders/panels we can't flag): `Harness` stashes the original `Attributes`, swaps in the proxy, restores on expand. Same hide effect, node never moved/removed.

`PhyBase.CreateAttributes()` now returns `PhyComponentAttributes` (new, in Attributes/) = collapse-aware `GH_ComponentAttributes` covering all stock-attr components. Bespoke attribs each got a `CollapseGuard.TryCollapseLayout`/`IsCollapsed` guard: `GripLinkAttrib` (covers Feedback + PyTransmitter + ZoomGuid subclasses), `FeedbackCollectorAttrib`, `PickerAttrib`. **PrompterAttrib deliberately NOT guarded** (Prompter is the mutually-exclusive alt to Chatbox, has a canvas TextBox overlay — won't be a Chatbox harness member).

Membership = managed group, three ways: auto-seeded from preset placement (`ChatWindow.HandlePlacePreset` → `Group.Add(PlaceResult.PlacedGuids)`), `CollapseSelectedIntoHarness`, `AddSelectedToHarness`/`RemoveSelectedFromHarness` (read `doc.SelectedObjects()`). Toggle in BOTH places: canvas chevron + Chatbox right-click menu (`AppendAdditionalMenuItems`), and chat-window menu item (`phbridge://togglecollapse` → `setState` now carries `collapsed`+`harnessCount`; App.svelte shows "Hide/Show harness (N)").

GOTCHA fixed during build: `ChatboxAttrib.Layout` calls `Group.RefreshCollapsePoint()` to glue members under a dragged Chatbox — must be change-gated (only re-push when the pivot moved) and must NOT call `canvas.Refresh()`, or it loops the paint. `ApplyState` (user-triggered, not in a paint pass) does refresh the canvas. Collapsed state re-applied on load via `_pendingApply` flag in `Read` → deferred `RhinoApp.Idle` from `SolveInstance`.

`GH_FontServer` was NOT resolvable in `Grasshopper.GUI` here (PrompterAttrib finds it via its other usings) — used a plain `Font` for the badge instead (renders in the zoomed Objects channel, scales fine).

Plan file: `C:\Users\tgaudin\.claude-personal\plans\do-some-research-i-foamy-russell.md`.
