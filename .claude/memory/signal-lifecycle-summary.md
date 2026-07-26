---
name: signal-lifecycle-summary
description: "Pointer to the authoritative signal-lifecycle doc, plus what the 2026-06-10/11 rework deleted and the multimodal ContentBlocks extension"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:05:06.963Z
---

**Authoritative doc: `planning/data-marshalling.md` in the repo — read that, not memory.** CLAUDE.md summarises it accurately too. This note exists only to record what was REMOVED (so a stale design never gets reintroduced) and one extension.

Bool triggers, momentary pulses, SHA-256 change detection, and Data/Feedback output ports are **gone** (commits 91c83c5, 93ee097, d6a086c). Events are latched, sequence-numbered, consume-once `PhySignal`s (`Core/Signals`); the signal carries the event AND its data (Success Signal(0) / Fail Signal(1), one wire per hop). Two-layer bases: `StatefulComponentBase` (state machine, ObserveSignalInputs/Consume*/Latch*, wall-clock-honest `ScheduleStateSolve` funnel) → `RoutingComponentBase<TData>` (push/read/latch; async = `AutoScheduleRead=false` + `RequestReadPass()`). Nothing in the lifecycle serializes — components reopen Empty.

**Multimodal extension (2026-06-13):** `PhySignal` also has an optional `IReadOnlyList<MessageContent> ContentBlocks` (init prop, default empty; `Mint` takes an optional `contentBlocks` arg; `LatchSuccess` forwards it). The string `Payload` stays the text/trace carrier; `ContentBlocks` rides alongside when a turn is richer than text — needed because the only wire from the prompt source to the Conversation Log IS the signal, so an assembled image+text user turn cannot use a parallel data wire. This couples `Physalia.Core.Signals` → `ConvoInstruct` (MessageContent). Carrier discipline still holds: [[signal-carrier-discipline]].

Non-repo leftovers: [[routing-trigger-system]]. Locked decisions not to relitigate + verification status: [[trigger-state-machine-status]].
