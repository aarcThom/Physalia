---
name: compaction-tool-pairing-fix
description: "2026-07-29: compaction orphaned tool_use blocks and killed a staged build (Anthropic 400); Reassemble made symmetric, KeepHeadAndTail never splits an exchange, LlmCall repairs before sending"
metadata: 
  node_type: memory
  type: project
  originSessionId: a85bdf84-01fa-4ea6-8b9a-84c171ccde49
  modified: 2026-07-30T06:48:06.105Z
---

**2026-07-29 — the staged White House build died at stage 3 of 5, and it was our bug, not an LLM quirk.**

Mechanism (proven from `signal_trace.txt`, arithmetic and tool ids both matched): at 11 turns the
Anchored Window fired for the first time with its shipped defaults `Keep First = 2 / Keep Last = 8`.
`KeepHeadAndTail` kept turns 1–2 + 4–11, dropping turn 3 — the `tool_result` turn answering turn 2's
three `search_components` calls. `CompactionInvariants.Reassemble` stripped orphan **tool_results**
only (no reverse pass), so the three calls survived unanswered; then its same-role merge fused turns
2 and 4 into ONE assistant turn carrying all four `tool_use` blocks (10 → 9, matching the trace's
`Compacted 11 → 9`). Anthropic 400: `tool_use ids were found without tool_result blocks`.

`Keep First = 2` is the worst default for a tool-using rig — index 1 is the first assistant turn,
which in this pipeline is almost always the first tool call, so the head cut splits an exchange **by
construction**. `KeepHeadAndTail` is also the only strategy that cuts in the middle; the others trim
from the front, where the one implemented direction happens to be the one that matters.

What landed:
- **`Reassemble` is now symmetric and runs to a fixed point.** Strips unanswered `ToolCallContent`
  as well as orphan `ToolResultContent`, and enforces the rule providers actually apply — every call
  answered by the **immediately following** turn, which the merge step can break on its own.
- **`KeepHeadAndTail` never splits an exchange**: `Keep First` is a MAXIMUM; the head shrinks off any
  assistant turn whose results are in the dropped middle, so an exchange is kept or dropped whole.
- **`ToolPairing.FindProblems`** (new, `Core/ConvoInstruct/`) — read-only diagnosis in plain language.
  `LlmCall.PushSolve` runs it and repairs via `Reassemble` before sending, with a loud canvas
  warning. Any future upstream defect now costs a warning and a shorter prompt, not a dead loop.
- Provider errors are readable (`HttpErrorMapper.Describe` + `MapErrorType`); `LlmErrorKind` now
  reaches `LlmCall`, which frames an `InvalidRequest` as a Physalia-side fault the model cannot fix.

**Deliberately NOT done: wiring `LLM Call.Fail Signal` back to feedback.** The failure carries no
`Instructions`, so a retry re-reads the same log → bit-identical 400, uncapped, and each round
*merges* the error JSON into the geometry-report turn. A 400 is never model-actionable.

Also fixed (same session): mid-stream SSE `error` events were ignored on **all three** protocols —
a partial answer was reported as a complete successful one; the false "spent its entire response
thinking" warning on tool-call rounds; Gemini's `functionResponse.name` (was sending the call id);
and `JsonNode.Parse(InputJson)` throwing on a zero-argument call.

**Gemini tool calling still does not work**: `ParseSseStreamAsync` never reads `functionCall` parts
and always passes null tool calls, so a Gemini tool round cannot complete. Separate job.

429 Core tests pass (15 new); both core fixes mutation-verified. **Live Rhino test still pending** —
the `.gha` copy fails while Rhino holds the plugin. Related: [[conversation-compaction]],
[[incremental-staged-building]], [[signal-carrier-discipline]].
