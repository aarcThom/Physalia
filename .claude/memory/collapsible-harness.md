---
name: collapsible-harness
description: "Collapse/expand a Chat's pipeline behind the single Chat proxy node (in-place visual collapse); new Harness class + folder"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3d4edd5b-d992-413c-af74-15bf81d67005
---

Collapsible harness feature (landed 2026-06-24, builds clean, **live-Rhino test pending**). A Chat can hide/show the pipeline components it owns, collapsing them behind the single Chat node (the Chat IS the proxy — distinct collapsed look: accent double-outline + member-count badge + a chevron toggle at top-right). Related: [[chat-widget]], [[chat-window]], [[preset-placement]], [[chatbox-switcher-row]].

**Mechanism = in-place visual collapse** (NOT a sub-document cluster, NOT off-canvas teleport — both rejected; cluster route would force every side-effect component to climb `OnPingDocument().Owner` + re-validate the whole signal lifecycle in a sub-doc). Members stay in the main document and keep solving; only rendering changes. GH has no native per-component hide flag (`IGH_PreviewObject.Hidden` = geometry preview only), so it's simulated.

New folder `src/Physalia.GH/Harness/`:
- `Harness.cs` (class `Harness`, namespace `Physalia.GH.Harness`) — owns member `HashSet<Guid>`, collapsed flag, swapped-attrs map; `Add/Remove/SetCollapsed/Toggle/ApplyState/Prune/RefreshCollapsePoint/Write/Read`. Persisted with the Chat. NOTE namespace==class name clash, so Chat aliases `using HarnessGroup = Physalia.GH.Harness.Harness;` and exposes it as `Chat.Group`.
- `CollapseGuard.cs` — static; Physalia (`PhyBase`) members hide via a `HarnessCollapsed`+`HarnessCollapsePoint` flag on PhyBase; their attributes shrink the component + all param grips to a zero-size rect at the shared collapse point (the Chat pivot) and skip Render. All members share ONE point so internal wires become zero-length (vanish); only boundary wires to external nodes leave a short stub. Pivot is never mutated → expand restores for free.
- `CollapsedProxyAttributes.cs` — `GH_Attributes<IGH_DocumentObject>` zero-render wrapper for **non-Physalia** members (native sliders/panels we can't flag): `Harness` stashes the original `Attributes`, swaps in the proxy, restores on expand. Same hide effect, node never moved/removed.

`PhyBase.CreateAttributes()` now returns `PhyComponentAttributes` (new, in Attributes/) = collapse-aware `GH_ComponentAttributes` covering all stock-attr components. Bespoke attribs each got a `CollapseGuard.TryCollapseLayout`/`IsCollapsed` guard: `GripLinkAttrib` (covers Feedback + PyTransmitter + ZoomGuid subclasses), `FeedbackCollectorAttrib`, `PickerAttrib`. **PrompterAttrib deliberately NOT guarded** (Prompter is the mutually-exclusive alt to Chat, has a canvas TextBox overlay — won't be a Chat harness member).

Membership = managed group, three ways: auto-seeded from preset placement (`ChatWindow.HandlePlacePreset` → `Group.Add(PlaceResult.PlacedGuids)`), `CollapseSelectedIntoHarness`, `AddSelectedToHarness`/`RemoveSelectedFromHarness` (read `doc.SelectedObjects()`). Toggle in BOTH places: canvas chevron + Chat right-click menu (`AppendAdditionalMenuItems`), and chat-window menu item (`phbridge://togglecollapse` → `setState` now carries `collapsed`+`harnessCount`; App.svelte shows "Hide/Show harness (N)").

Undo/redo for add/remove: `Harness.Add`/`Remove` return the actual membership delta; Chat `RecordMembershipUndo` pushes a `HarnessMembershipUndoAction : GH_UndoAction` via `doc.UndoServer.PushUndoRecord(name, action)`. The action reverses the exact delta through `Group.Add`/`Remove` (which already handle hide/show), so undoing an add removes only what that add introduced. Collapse/expand toggle is NOT undoable (view state); `CollapseSelectedIntoHarness` records only its membership-add delta (partial undo of that combo is a known nuance).

Preset = collapsed (2026-06-24): predefined workflows land **collapsed**. `ChatWindow.HandlePlacePreset` calls `_component.CollapseHarnessDeferred()` after seeding (only when `Group.Count > 0`); that schedules a one-shot `RhinoApp.Idle` → `_group.SetCollapsed(true)`, deferred so the placement solution has settled and members are laid out (valid proxy pivot + native attribute swaps) before hiding.

Bugfix (2026-06-24): a plain Chat added as a *member* of another harness stayed visible on collapse — `ChatAttrib` was the one bespoke attrib without the collapse guard (the Chat is normally the proxy, not a member). Added `CollapseGuard.TryCollapseLayout`/`IsCollapsed` at the top of `ChatAttrib.Layout`/`Render` (member-hide runs before the proxy-chrome logic, so the owner vs member roles stay independent).

Refinement (2026-06-24): a Chat is a harness **only when it owns members** (`Group.Count > 0`) — no new flag. An empty Chat renders as a plain node (`ChatAttrib` gates chevron + collapsed decoration on Count>0) and its menu shows only the two "…into/to Harness" entry items; collapse/expand + remove appear once it has members. No-nesting rules: `Harness.Add` → `CanContain` bars (a) a Chat that already owns a harness (Count>0) and (b) anything already a member of another Chat's harness (single-membership), via static `Harness.IsMemberOfAnyHarness(doc, guid, exceptOwner)` + `Harness.Contains`. Reverse guard: Chat `IsMemberOfAnotherHarness()` greys out the entry menu items when this Chat is itself a member of another harness (a plain-member can't become an owner). Presets never absorb a peer Chat — `ChatWindow.HandlePlacePreset` filters Chats out of `PlacedGuids` before `Group.Add`. Removing the last member auto-expands + clears collapsed (`ResetIfEmpty`). User chose "bar only harness-OWNING Chats" (plain Chat may be a member).

GOTCHA fixed during build: `ChatAttrib.Layout` calls `Group.RefreshCollapsePoint()` to glue members under a dragged Chat — must be change-gated (only re-push when the pivot moved) and must NOT call `canvas.Refresh()`, or it loops the paint. `ApplyState` (user-triggered, not in a paint pass) does refresh the canvas. Collapsed state re-applied on load via `_pendingApply` flag in `Read` → deferred `RhinoApp.Idle` from `SolveInstance`.

`GH_FontServer` was NOT resolvable in `Grasshopper.GUI` here (PrompterAttrib finds it via its other usings) — used a plain `Font` for the badge instead (renders in the zoomed Objects channel, scales fine).

Plan file: `C:\Users\tgaudin\.claude-personal\plans\do-some-research-i-foamy-russell.md`.
