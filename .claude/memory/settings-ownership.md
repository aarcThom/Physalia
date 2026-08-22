---
name: settings-ownership
description: Every user-set setting (cluster/catalog selection, units override, snapshot wording, tool on-off) is serialized on the component it configures, not on the Conversation Log, so it ships inside a preset; the log is a live-walked façade
metadata:
  node_type: memory
  type: project
---

**2026-08-21.** Settings were already serialized — but almost all of them on the **Conversation Log**,
which is the wrong owner if the point is *distribution*. Moved each one onto the component it
configures. See CLAUDE.md § "Settings live on the component they configure" for the table.

**Why it matters (the actual requirement):** an author configures a pipeline once and ships it. A
setting on the component travels with a copy, is saved in the `.gh`, and rides inside a **preset**;
a setting on the log did none of that for the grounder next to it, and was lost if the log was
replaced.

## What moved where
- Catalog tab/panel selection + expose-signatures → `ComponentCatalogGrounder` (which already owned
  include-legacy — this made it consistent).
- Cluster selection → `ClusterGrounder`. Units override → `DocumentUnitsGrounder`. Snapshot wording →
  `SnapshotToolComponentBase` (beside the send-or-attach flag it belongs with).
- **Tools on/off → each `LlmToolComponentBase` node** ("Advertise To The Model", right-click + chat
  window). This one KILLED the old name-keyed `ToolsSelection` storage: two nodes advertising the same
  tool name used to share one checkbox. `ToolsSelectionOrNull` is now DERIVED ("which nodes are on",
  null when all are) so the UI contract and the Svelte side needed no change at all.

## The three traps
1. **A parked tool must stay listed.** `ToolsInUse` emits only advertised nodes on the wire but exposes
   `ScannedTools` (the whole in-use set) for the chat window — and `HasToolsGrounding` had to stop
   meaning "`_liveTools` is non-empty", or switching the last tool off hides the page that switches it
   back on. The advertise flag also goes into the grounder's SIGNATURE, or its SolutionEnd watch never
   senses the flip.
2. **Resolve owners LIVE, never cache on solve.** The façade walks the Grounding/Human Tools input
   sources on each call (through bare relay params, depth-limited). A solve-time cache is empty exactly
   when it is needed: the chat window's tick between solves, and `GhJsonBridge.RestoreGroundingSelection`
   writing a selection onto components it has only just placed. That ghjson extension still rides on the
   ConversationLog and still works — through the façade.
3. **Migration must be deferred and funnelled.** `ConversationLog.Read` keeps reading its old keys into
   `_legacy*`; `ApplyLegacySettings` hands them down on the next solve (the first moment the wires are
   known) via `ScheduleStateSolve` — NOT a raw `ScheduleSolution`, since GH keeps ONE document schedule
   and a raw post races the latch. Tool flags are restored with `RestoreAdvertise` (quiet), because
   asking a node for its own solution inside a scheduled callback is what must not happen. The keys are
   never written again. **The shipped presets are exactly this case** — saved before the move.

`SettingArchive` states the null-vs-empty archive discipline once (null = never configured, empty =
include nothing, and GH's archive has no null → a `<key>Set` flag beside the value).

Not yet run in Rhino. Related: [[grounding-on-recorder]], [[tools-in-use-component]], [[harness-subdocument]].
