---
name: signal-lifecycle-summary
description: "Pointer to the authoritative signal-lifecycle doc, plus what the 2026-06 rework DELETED (so no stale design gets reintroduced) and the locked decisions not to relitigate"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-08-21T00:00:00.000Z
---

**Authoritative doc: `planning/data-marshalling.md` in the repo — read that, not memory.** CLAUDE.md
summarises it accurately too. This note exists only to record what was REMOVED (so a stale design is
never reintroduced) and the decisions Thomas locked.

## What is GONE — never reintroduce
Bool triggers, momentary pulses, SHA-256 change detection, and Data/Feedback string output ports
(commits 91c83c5, 93ee097, d6a086c, June 2026). Events are latched, sequence-numbered, consume-once
`PhySignal`s (`Core/Signals`); the signal carries the event AND its data (Success Signal(0) / Fail
Signal(1), one wire per hop). Two-layer bases: `StatefulComponentBase` (state machine,
ObserveSignalInputs/Consume*/Latch*, wall-clock-honest `ScheduleStateSolve` funnel) →
`RoutingComponentBase<TData>` (push/read/latch; async = `AutoScheduleRead=false` +
`RequestReadPass()`). Nothing in the lifecycle serializes — components reopen Empty, and the old
serialization keys (`State`, `DataOut`, `FeedbackOut`, `LastTrigger`) are gone from the codebase
entirely; don't "fix" an old `.gh` that still contains them.

**Why pulses died (the root cause worth remembering):** `GH_Document` keeps ONE schedule timer —
shorter delays REPLACE longer ones and every pending callback fires at the next solution. So
per-component `ScheduleSolution` delays collapse the moment more than one component is in flight,
and the feedback loop raced (feedback recorded before the LLM response → the API rejected the
conversation). Correctness is by identity now, never by timing.

## Locked decisions (don't relitigate)
- Bool triggers are fully replaced between Physalia components. A bare Button/Toggle into a Signal
  input is a **hard error** (it has no payload); the one sanctioned manual mint is Construct Signal's
  dedicated Boolean Trigger input.
- The signal carries the data — never a parallel string wire, and never a new typed carrier field
  beyond Payload / ContentBlocks / Instructions: [[signal-carrier-discipline]].
- Nothing in the lifecycle persists; components always reopen Empty.
- Conversation Log turn type comes from **input identity**, never conversation parity (it now has
  seven inputs — see CLAUDE.md for the order, which is a saved-document contract).
- Feedback arriving when the last turn is already User → merge into it
  (`Conversation.MergeIntoLastUserMessage`, which must preserve `IsFeedback`).
- `SolveDelayMs=500` visible pacing is cosmetic and wall-clock honest; Construct Signal latches with
  NO delay (it is a source, not a hop).
- **PyTransmitter deliberately does NOT clear its linked target** on Clear Outputs / unlink — the
  pushed Python stays in the target component. A requirement, not an omission (unlike Schema
  Validator / SchemaTranslator, which own only their own latches).

## Extension: multimodal (2026-06-13)
`PhySignal` also carries an optional `IReadOnlyList<MessageContent> ContentBlocks` (init prop,
default empty; `Mint` takes an optional `contentBlocks` arg; `LatchSuccess` forwards it). `Payload`
stays the text/trace carrier; `ContentBlocks` rides alongside when a turn is richer than text —
needed because the only wire from the prompt source to the Conversation Log IS the signal. This
couples `Physalia.Core.Signals` → `ConvoInstruct` (MessageContent).
