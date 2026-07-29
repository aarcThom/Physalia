---
name: view-snapshot-human-tool
description: "View Snapshot human tool (2026-07-28) — geometry-free sibling of Geometry Snapshot: captures the active viewport as-is, wired = armed; shared SnapshotToolComponentBase; AcceptsPromptImages fixes the attach-lane image drop"
metadata: 
  node_type: memory
  type: project
  originSessionId: 3dee4faf-f9c3-48ec-895b-bdfb863d3da7
  modified: 2026-07-29T04:11:01.377Z
---

# View Snapshot human tool (2026-07-28)

Thomas's ask: a human tool that "simply takes a snap of the current window's view and does not rely on
any geometry". Built as a **full mirror** of Geometry Snapshot (his call over two leaner options), minus
the geometry dependence.

**The one-line difference that made it cheap:** `ViewportSnapshot.TryCapture(bounds, …)` already skips its
zoom when `bounds` is invalid. So the view path is `TryCapture(BoundingBox.Unset, …)` — no
`GeneratedGeometryScan`, no `bounds.IsValid` bail, camera untouched.

**Shape**
- Core: `ViewSnapshotTool(Message, SendWithMessage = true)` + its own `DefaultMessage` (says nothing about
  "generated" geometry — it makes no claim about where what you see came from).
- GH: `ViewSnapshot` (GUID B7E3D14A-9C62-4F05-8A7D-2E6B4C1F93D8). Both snapshot components now derive from
  new **`SnapshotToolComponentBase`**, which owns `_sendWithMessage`, the "Send With Default Message" menu
  item, `SetSendWithMessage`, and the `SendWithMessage` GH_IO key (key unchanged → old files still read).
  `ConversationLog.SetSnapshotSendsMessage<T>()` is the generic setter both public wrappers call.
- No icon PNG: `PhyBase.Icon` falls back to `brain.png` — same as Geometry Snapshot / Add Image.
- Chat: `SendViewSnapshotFromWindow` / `TryCaptureViewPng`; both send paths share `LatchSnapshotTurn`.
- Bridge verbs: `sendviewsnapshot`, `attachviewsnapshot`, `setviewsnapshotmessage`, `setviewsnapshotsends`;
  host hook `attachViewSnapshot`; `PushAttachment(hook, png)` shared. `TryReadSnapshotMessage` shared parse
  (returns false on malformed JSON so a bad payload never silently clears an override).
- UI: 4 new state fields (**no** `viewSnapshotGeometryPresent` — wired is armed), a `CameraIcon` button in
  the top rail that is never greyed, a `viewsnapshot` page + Human Tools pill in the grounding panel, and a
  **third Composer image lane** (`source: 'user' | 'snapshot' | 'viewsnapshot'`, gated by a `granted` map so
  flipping one tool to send-mode never strands the other's pending attachment).

**Pre-existing bug fixed on the way:** `SubmitJsonPayload` dropped ALL prompt images unless Add Image was
wired — which silently broke Geometry Snapshot's own attach mode. Now gated on
`ConversationLog.AcceptsPromptImages` (Add Image **or** either snapshot tool in attach mode).

Status: `dotnet build src/Physalia.slnx -c Debug` clean, 397 tests green, `npm run check` 0 errors (the 3
new `state_referenced_locally` warnings mirror the 3 existing snapshot ones — deliberate local-source-of-
truth state). **Live Rhino test pending.** Related: [[human-tools-split]], [[geometry-snapshot-grounding]].
