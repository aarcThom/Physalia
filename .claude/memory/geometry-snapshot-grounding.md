---
name: geometry-snapshot-grounding
description: Geometry Snapshot grounding — composer geometry BUTTON sends a viewport snapshot + predefined editable message as its own message (never auto-attached); shown only while grounding wired and transmitter-generated geometry exists
metadata: 
  node_type: memory
  type: project
  originSessionId: 0051c9fe-ee20-44ce-86cd-9cb93ae1cfd0
---

# Geometry Snapshot grounding (2026-07-22, button rework 2026-07-23)

New grounding kind: the Geometry Observation guardrail's capture, sent on demand from the chat window. **Thomas's explicit design call: the snapshot is NEVER auto-attached to a typed prompt — the geometry button sends it as its own message, on press only.** (First cut auto-attached to every prompt; reworked same session.)

**How it works**
- `GeometrySnapshotGrounding(Message)` in Core `Grounding.cs` — `ToSystemPromptSection()` returns EMPTY (images can't ride the system prompt); carries `DefaultMessage` const. Grounding wired + generated geometry present = composer's geometry button appears.
- `GeometrySnapshotGrounder` (GH, Grounding tab, GUID D5B8F2A6-7E31-4C94-A0D8-3F6E1B9C5A27, no inputs) emits it → Conversation Log Grounding input.
- Button press → bridge verb `sendsnapshot` → `Chat.SendGeometrySnapshotFromWindow()` (InvokeOnUiThread — UI thread between solves, so viewport zoom/capture is safe WITHOUT the Idle deferral Geometry Observation needs): captures, mints ONE Prompt Signal whose turn is `TextContent(message) + ImageContent(png)`, payload = message. Quietly no-ops when unwired / no geometry / capture fails.
- "Generated geometry" = `Generation/GeneratedGeometryScan`: PyTransmitter `LinkedGuid` targets ∪ `GhJsonBridge.ModelPlacedGuids(doc)` (new accessor over the authored-placement ledger — covers full-graph placements AND ghpatch adds), unioned preview `ClippingBox`es. Same bounds frame the snapshot zoom.
- Capture/encode extracted to shared `Generation/ViewportSnapshot.TryCapture` (1568px cap); GeometryObservation refactored onto it.

**Message override** — mirrors the units-override pattern: `ConversationLog._snapshotMessageOverride` (nullable, serialized `SnapshotMessageSet`/`SnapshotMessage`, survives Clear), `SetSnapshotMessageOverride`, bridge verb `setsnapshotmessage` `{reset, message}`; state push fields `snapshotWired / snapshotGeometryPresent / snapshotDefaultMessage / snapshotMessage`.

**UI** — Grounding panel gained a Geometry Snapshot pill + page (textarea, blur-applies; empty or default-verbatim text resets override). Composer shows an accent `Axis3d` BUTTON directly ABOVE the add-image icon only while `snapshotWired && snapshotGeometryPresent`; pressing it sends the snapshot (message editing lives only in the grounding panel).

Status: builds clean, 300 tests green. Live Rhino test pending. Related: [[signal-carrier-discipline]], [[iterative-canvas-editing]].
