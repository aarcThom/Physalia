# Data Marshalling & the Component State Machine

How data and triggers move between Physalia components, and the two-layer base-class
scheme that formalizes it. This is the reference for building any new component that
participates in the pipeline.

The design goal is **visible data flow**: a human watching the canvas should be able to
trace where data currently is in the DAG. Every component advertises its lifecycle state
on the canvas and holds a deliberate delay at the end of each solve so the hop from one
component to the next can be followed by eye.

---

## The Four States

Every pipeline component is always in exactly one of these states:

| State | Data out | Feedback out | Success trigger | Fail trigger | Canvas caption |
|---|---|---|---|---|---|
| **Empty** | empty | empty | false | false | *(blank)* |
| **Active** | empty (cleared on entry) | empty | false | false | `Active…` |
| **SolveSuccess** | latched result | empty | **pulses true for one solve** | false | `Success` |
| **SolveFailure** | empty | latched feedback | false | **pulses true for one solve** | `Failed` |

- **Empty** — no data has passed through: the component is fresh on the canvas, has never
  been triggered, or was manually cleared via its right-click Clear item.
- **Active** — entered on a trigger rising edge. Stale outputs blank *immediately on
  entry*, so the wires go dark while the component works. The work may be instantaneous
  (Auditor's schema validation), deterministic-but-deferred (PyTransmitter waiting for a
  linked Python component to re-solve), or asynchronous (Reasoner's streaming API call).
  Regardless, a visible delay (`SolveDelayMs`, currently **500 ms**) is held between
  completion of the work and the latch, once per run.
- **SolveSuccess** — the run completed with no error. The Data output latches the result
  and persists until the next Active entry or a manual Clear. The success trigger goes
  true for exactly one solve, then resets — downstream components fire off this rising
  edge.
- **SolveFailure** — the run completed but produced an error (Python script errors for
  PyTransmitter, schema violations for Auditor, API failures for Reasoner). Mirror image:
  Feedback latches, the fail trigger pulses once, Data stays empty.

Triggers on wires are plain booleans. A "trigger" is always a **rising edge**
(false → true); the pulse-then-reset pattern means an upstream component's momentary
`true` is the downstream component's rising edge. Last-trigger state is serialized so
reopening a file never spuriously fires.

---

## Solve Rhythm

A triggered run spans several Grasshopper solves, sequenced with
`GH_Document.ScheduleSolution`:

```
Solve A  (trigger rising edge)
         EnterActive: clear stale outputs, caption "Active…"
         PushSolve: side effects (push code into linked component, start API task, …)
         → schedule read @1ms (sync) — or the async completion callback schedules it

Solve B  (@1ms, may repeat)
         IsReadReady?  no  → retry @1ms (up to 10 attempts)
                       yes → schedule the latch @SolveDelayMs (500ms, ONCE — not per retry)

Solve C  (@500ms)
         ReadSolve → latch Data or Feedback
         State = SolveSuccess / SolveFailure, caption updates
         trigger pulse TRUE on this solve  ← downstream rising edge lands here
         → schedule pulse reset @1ms

Solve D  (@1ms)
         pulse → false; latched data persists until the next run or a clear
```

Key invariants:

- The 500 ms delay sits **after the work, before the latch** — for Reasoner that means
  after the API stream finishes; for PyTransmitter after the linked component has
  re-solved. Readiness retries stay at 1 ms so the delay is paid exactly once.
- The latch and the pulse happen in the **same solve**, so a downstream component that
  fires on the pulse reads fresh latched data.
- Read passes run only from the component's own scheduled callback (`_doRead` handshake);
  arbitrary intervening solves can never trigger an early read.
- Retry exhaustion (a linked target that never settles) still takes the 500 ms beat, then
  latches a timeout failure — the visual rhythm is identical on both paths.

---

## Two-Layer Architecture

### Layer 1 — `StatefulComponentBase : PhyBase`

`src/Physalia.GH/Components/StatefulComponentBase.cs`

Owns everything lifecycle-related and **nothing** I/O-related. It registers no
parameters and has no `SolveInstance`; subclass solves drive the transitions through a
small API:

| Member | Role |
|---|---|
| `SolveState State` | `Empty` / `Active` / `SolveSuccess` / `SolveFailure` |
| `SolveDelayMs = 500` | the visible end-of-solve delay (constant; may be user-exposed later) |
| `SuccessPulse` / `FailPulse` | true only on the latch solve |
| `DetectRisingEdge(bool)` | owns the serialized last-trigger value |
| `EnterActive()` | state → Active, pulses false, `ClearStateOutputs()`, caption update |
| `LatchSuccess(pulse = true)` | state → SolveSuccess; if pulsing, schedules the one-solve reset |
| `LatchFailure(pulse = true)` | mirror image |
| `ResetToEmpty()` | used by Clear and aborted runs; never pulses |
| `ScheduleStateSolve(ms, action)` | the single scheduling funnel — runs the mutation on a scheduled solution then expires the component; safe from background threads |
| `UpdateStateDisplay()` | pushes `MessageForState(State)` to the canvas caption |

Hooks a subclass implements/overrides:

- `ClearStateOutputs()` *(abstract)* — wipe latched output backing fields only; never
  domain state.
- `OnCleared()` — extra reset for the menu Clear (lifecycle flags, domain data such as a
  conversation log).
- `ClearMenuText` — caption for the Clear menu item (`"Clear Outputs"` default,
  `"Clear Conversation"` on Recorder).
- `RestoreLatchedStateOnLoad` — whether a persisted Success/Failure survives file reload
  (false when the latched payload itself is not serialized).
- `MessageForState(state)` — canvas captions.

`pulse: false` on the latch methods is the **quiet outcome**: state and caption update,
but no trigger fires downstream. This is what lets Recorder participate without looping
(see below) and what a "soft failure" (nothing to do) uses.

### Layer 2 — `RoutingComponentBase<TData> : StatefulComponentBase`

`src/Physalia.GH/Components/RoutingComponentBase.cs`

The standard **5-port routing contract** for components that route a string result either
forward or back:

```
Inputs                          Outputs
  Data        (TData, index 0)    Data            (string — latched on success)
  …extras…                        Success Trigger (bool — one-solve pulse)
  Trigger     (bool, last)        Feedback        (string — latched on failure)
                                  Fail Trigger    (bool — one-solve pulse)
```

Subclasses implement only:

- `RegisterDataInput` / `RegisterAdditionalInputs` — typed inputs.
- `TryGetData` — parse the Data input; returning false means the trigger is ignored
  (nothing to process, no state change).
- `PushSolve` — side effects that must settle before the result can be read. Empty for
  synchronous components.
- `ReadSolve` — produce a `RoutingResult.Ok(data)` or `RoutingResult.Fail(feedback)`
  after the document has settled.
- `IsReadReady` *(optional)* — defer the read until e.g. a linked component has
  re-solved; the base retries at 1 ms up to 10 times.
- `AutoScheduleRead` *(optional)* — return false for async components and call
  `RequestReadPass()` from the completion callback instead.
- `OnSolveTick` *(optional)* — per-solve inputs outside the lifecycle (Reasoner's Cancel).

Layer 2 also owns serialization of the latched payloads (`DataOut` / `FeedbackOut`) and
legacy-file compatibility: files saved before the state machine existed infer their state
from whichever payload is non-blank.

### Current implementations

| Component | Shape | Notes |
|---|---|---|
| **Auditor** | sync | empty `PushSolve`; `ReadSolve` extracts + validates JSON against the schema |
| **PyTransmitter** | two-pass | `PushSolve` injects code into the linked Python component and expires it; `IsReadReady` waits for it to compute; `ReadSolve` harvests runtime errors |
| **Reasoner** | async | `AutoScheduleRead => false`; `PushSolve` starts the streaming task; the completion callback calls `RequestReadPass()`; Cancel aborts via `AbortReadPass()` → back to Empty |
| **Recorder** | Layer 1 directly | see below |

---

## Recorder: Layer 1 Without the Routing Contract

Recorder shares the state machine but **not** the 5-port contract — its outputs are typed
(`Instructions`, `Recorded History`) and its trigger semantics are deliberately selective.

Two constraints shape its mapping:

1. **Appends happen on the rising-edge solve, not after the delay.** Pulse-borne inputs —
   a FeedbackCollector's Physalia Prompt — only exist during the triggering solution.
   Recorder captures and appends immediately on the rising edge; only the output latch
   and the pulse are deferred by the 500 ms delay.
2. **Assistant turns are quiet successes.** Recorder's Trigger output pulses only when a
   *user* message was appended. Recording the Reasoner's own response latches with
   `LatchSuccess(pulse: false)` — caption says "Success", outputs latch, but no pulse
   fires. Otherwise the wiring `Recorder.Trigger → Reasoner.Trigger` plus
   `Reasoner.SuccessTrigger → Recorder.Trigger` would loop forever.

Outcome mapping per triggered run:

| Append outcome | Latch | Pulse |
|---|---|---|
| User message appended (Prompt or Physalia Prompt) | SolveSuccess | **yes** — Reasoner fires |
| Assistant message appended (response or tool calls) | SolveSuccess | no (quiet) |
| Nothing new to record (dedupe, blank inputs) | SolveFailure + runtime warning | no (quiet) |

Other Recorder specifics:

- The Instructions output is a **snapshot latched at trigger time** — editing the System
  Prompt mid-state does not change the output until the next trigger.
- The Conversation override input (compaction) is processed on every solve, outside the
  state machine — it is a data transformation, not a run outcome.
- `ClearMenuText => "Clear Conversation"`: the base Clear wipes both latched outputs *and*
  the conversation/history (via `OnCleared`), returning to Empty.
- `RestoreLatchedStateOnLoad => false`: the conversation is not serialized, so Recorder
  always reopens Empty rather than claiming a Success it cannot back up.

---

## Serialization Rules

- Layer 1 persists `State` and `LastTrigger`. **Pulses are never persisted** — a file can
  never re-fire its downstream chain on open.
- A file saved mid-Active reopens as Empty (in-flight work is unrecoverable).
- Success/Failure reopen latched (caption + payload restored) only when the component
  also serializes the payload (`RestoreLatchedStateOnLoad`, true for routing components,
  false for Recorder).
- Legacy files (pre-state-machine) infer: `DataOut` non-blank → SolveSuccess,
  `FeedbackOut` non-blank → SolveFailure, else Empty.

---

## Out of Scope (For Now)

- **Feedback / FeedbackCollector** stay outside the state machine — they are a wireless
  transport, not data-latching processors. Their one-solve pulse pattern composes with
  the scheme: the Collector's pulse lands in Recorder's rising-edge solve, before the
  delay, so nothing is lost. If a canvas state display is ever wanted, FeedbackCollector
  could adopt Layer 1 trivially (`Inject` → Active, emit solve → Success).
- **Composer** is a plain deterministic component with no trigger; it does not need the
  lifecycle.
- **User-facing delay control** — `SolveDelayMs` is a single named constant precisely so
  it can later be exposed (per-document setting or per-component input) without touching
  the transition logic.

---

## Building a New Pipeline Component

1. **Routes a string result forward/back on a trigger?** Inherit
   `RoutingComponentBase<TData>`; implement `TryGetData` + `ReadSolve` (sync), add
   `PushSolve`/`IsReadReady` (two-pass) or `AutoScheduleRead => false` +
   `RequestReadPass()` from your callback (async). You get the full state machine, the
   5-port contract, captions, Clear, and serialization for free.
2. **Trigger-driven but with bespoke outputs or pulse rules?** Inherit
   `StatefulComponentBase` (the Recorder pattern): call `DetectRisingEdge` →
   `EnterActive()` → do the work → `ScheduleStateSolve(SolveDelayMs, …)` →
   `LatchSuccess/LatchFailure` with the pulse flag your semantics require; implement
   `ClearStateOutputs` for your latched fields.
3. **No trigger, purely deterministic?** Plain `PhyBase` (the Composer pattern).
