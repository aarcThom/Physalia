---
name: merge-signal-join
description: "Merge Signal (Control Flow, 2026-08-16) is a JOIN not a passthrough, because parallel Physalia branches latch on separate scheduled solves."
metadata: 
  node_type: memory
  type: project
  originSessionId: 0956dedc-1e49-4d54-bc62-8bc7fd2a716c
  modified: 2026-08-17T08:28:47.297Z
---

`MergeSignal` (`src/Physalia.GH/Components/ControlFlow/MergeSignal.cs`, GUID `3E9B7C41-…5B60`)
merges two or more signals into one. Variable inputs (min 2) via the zoom +/- icons, added and
removed at the **END only**.

**Why a join and not a per-solve passthrough** (the decision, made with the user 2026-08-16): almost
every Physalia emitter latches on a `ScheduleStateSolve` follow-up, so two parallel branches reach
the merge in *different* solutions. Merging "whatever is on the wire this solve" would emit once per
branch — and downstream of a Conversation Log, log one turn per branch, which is exactly what
merging is for avoiding. So it holds the newest signal per wired input and mints ONE signal only
once the whole **wired** set is holding one. Accepted cost: a round where a wired branch stays
silent parks at `1 / 2` (caption) until it fires; `Clear Outputs` abandons it.

**Why inputs only append/remove at the end:** the per-input hold AND
[[signal-lifecycle-summary]]'s consume-once `_marks` in `StatefulComponentBase` are keyed by
**parameter index**. An insertion in the middle shifts both — a wire's mark would be compared
against another slot's high-water and could replay or swallow an event. Router gets away with
mid-list insertion only because its variable params are *outputs* (no marks). Any future
variable-input signal component inherits this constraint.

Merge order is global sequence = causal order ([[signal-carrier-discipline]]): payloads
blank-line-joined (blanks skipped), `ContentBlocks` combined in the same order, `Instructions`
= the newest non-null (a whole inference context does not concatenate), outcome = Failure if any
part failed.

## 2026-08-17 — the aggregation bug: joining payloads while concatenating blocks LOSES text

**Proven live in Rhino** (a staged parametric-wall build, `signal_trace.txt` + `phy_chat.txt`): a
Geometry Report merged with a Geometry Observation reached the model as **the image alone** — the
whole report, plan digest and base checksum silently gone. The merge itself was innocent; the trace
showed signal #77 carrying both. The loss was at the Conversation Log.

**The invariant that was broken.** A signal carrying `ContentBlocks` carries them as the WHOLE turn;
`Payload` is then only their text trace. Producers obey it (Geometry Observation: image block +
*blank* payload with no Message, text block + mirroring payload with one), so
`ConversationLogBuilder` reads non-empty blocks as authoritative and uses the payload for tracing
only. Aggregating by "join the payload strings, concatenate the block lists" breaks it: a text-only
part's text ends up in the payload and in **no block**, so the builder records the blocks and drops
the text. **The Feedback Collector had the identical shape** — same bug, one hop later.

**Fix:** `Physalia.Core/Signals/SignalAggregation.cs` — one pure `Combine(parts, separator)` used by
both. A part with blocks contributes them verbatim (a `tool_use_id` must survive); a part with none
contributes its payload **as a `TextContent` block**. Blocks are only materialised if some part
actually carried them, so an all-text merge stays text-only and behaves exactly as before.
Defence in depth: `ConversationLogBuilder.WithPayloadText` restores a leading text block when a
user-side signal's blocks contain no `TextContent` but its payload is non-blank — applied to the
Prompt and Feedback branches only, **never to tool turns** (a `tool_result` block must lead its user
message). 10 new tests; 462 pass.

Builds clean; the join mechanics themselves are now **proven in Rhino**.
