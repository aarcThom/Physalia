---
name: signal-carrier-discipline
description: PhySignal carries exactly Payload + ContentBlocks + Instructions — never add carrier fields for arbitrary types
metadata: 
  node_type: memory
  type: feedback
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

`PhySignal` (`Physalia.Core/Signals/PhySignal.cs`) carries exactly three things and **must not grow more**:
- `Payload` (string) — the text trace / feedback string.
- `ContentBlocks` (IReadOnlyList<MessageContent>) — a richer-than-text user turn (e.g. inline images): the Prompter→Conversation Log hop.
- `Instructions` (Instructions?) — the full inference context (system prompt + conversation): the Conversation Log→LLM Call hop, where the trigger IS the data.

**Why:** the signal is the single inter-component wire ("the signal is the event"). Each of those three is a genuine pipeline *event payload*. Adding a typed field for any other data type turns PhySignal into a god-object and dilutes the model — arbitrary data belongs on **typed wires/inputs**, not bolted onto the signal.

**How to apply:** when a component needs to move data that isn't one of those three, use a typed parameter (a `Param_X`/`GH_X`), NOT a new `PhySignal` field. If you think you need a fourth carrier, stop and reconsider the wiring. (History: a short-lived `Conversation` carrier was added for a compaction loop-back, then **removed** — folded into `Instructions` — when compaction moved to the inline forward path. Don't reintroduce per-type carriers.) `GH_Signal.CastTo` exposes the carried Instructions/Conversation/text so typed inputs consume a signal without manual deconstruction — extend the *cast*, not the carrier set. Documented in CLAUDE.md (Signals section). See [[conversation-compaction]].
