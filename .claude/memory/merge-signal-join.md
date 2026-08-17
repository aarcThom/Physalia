---
name: merge-signal-join
description: "Merge Signal (Control Flow, 2026-08-16) is a JOIN not a passthrough, because parallel Physalia branches latch on separate scheduled solves."
metadata: 
  node_type: memory
  type: project
  originSessionId: 0956dedc-1e49-4d54-bc62-8bc7fd2a716c
  modified: 2026-08-17T06:34:08.748Z
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
blank-line-joined (blanks skipped), `ContentBlocks` concatenated in the same order, `Instructions`
= the newest non-null (a whole inference context does not concatenate), outcome = Failure if any
part failed.

Builds clean. **Not yet run in Rhino.**
