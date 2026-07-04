---
name: trigger-state-machine-status
description: "Signal-based event marshalling (consume-once, sequence-ordered, payload-only carriage) replaced bool pulses AND Data/Feedback string ports June 2026; user-locked decisions; manual Rhino verification pending"
metadata: 
  node_type: memory
  type: project
  originSessionId: bfb2ff92-c812-4e27-89de-9675e357c091
---

Marshalling architecture history on the `dev` branch:
- 2026-06-11 (morning): explicit Empty/Active/SolveSuccess/SolveFailure state machine
  (`StatefulComponentBase`) with bool trigger pulses.
- 2026-06-11 (later): pulses raced in the feedback loop (feedback recorded before the LLM
  response → API rejected the conversation). Root cause: **GH_Document keeps ONE schedule
  timer; shorter delays replace longer ones and ALL pending callbacks fire at the next
  solution** — per-component ScheduleSolution delays collapse whenever >1 component is in
  flight. Replaced pulses with **PhySignal**: latched, sequence-numbered events
  (`src/Physalia.Core/Signals/`), consume-once per receiver, processed in global sequence
  order (= causal order). Committed as 93ee097.
- 2026-06-11 (evening): **payload-only carriage** — Data/Feedback string outputs AND Data
  string inputs removed from routing components; the signal payload is the only carrier
  (one wire per hop). Routing outputs are now Success Signal (0) / Fail Signal (1).
  Lifecycle persistence dropped entirely (no State/DataOut/FeedbackOut serialization;
  everything reopens Empty). GH_Signal casts to text (= payload). New `Physalia > Signals`
  components: Construct Signal (manual mint, immediate latch, Failure flag) and
  Deconstruct Signal (passive tap, never consumes). FOUR RoutingComponentBase subclasses
  exist: Schema Validator, PyTransmitter, LLM Call, **SchemaTranslator** (Serializers/ — easy to
  forget). Authoritative doc: `planning/data-marshalling.md`.

Decisions Thomas locked (don't relitigate):
- Bool triggers fully replaced between Physalia components (pre-release, breaking OK);
  Buttons/Toggles still work via a non-minting cast sentinel + consumer edge detection
  (empty payload → payload-fed components warn and drop; use Construct Signal).
- Signal payload is the ONLY data carrier between pipeline components — never add a
  parallel string wire. LLM Call keeps its typed Instructions input (ignores payload).
- Nothing in the lifecycle persists; components always reopen Empty.
- Conversation Log: three dedicated signal inputs (Prompt/Response/Feedback) — turn type from
  input identity, never conversation parity; quiet (no-signal) latch on assistant turns.
- Feedback arriving when last turn is already User → merge into last user message
  (`Conversation.MergeIntoLastUserMessage`).
- SolveDelayMs=500 visible pacing kept, wall-clock honest, cosmetic-only; Construct
  Signal deliberately latches with NO delay (source, not hop).

Status: builds clean. Manual Rhino verification has NOT been run for either the Signal
rework or the payload-only simplification (race/retry loop, Construct→Schema Validator standalone,
text cast, busy lossless, Toggle semantics, reopen-Empty). No automated tests exist.
See [[physalia-repo-gotchas]].
