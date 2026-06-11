# Data Marshalling: Sequenced Signals & the Component State Machine

How data and events move between Physalia components: the **Signal** event model and the
two-layer state-machine base classes. This is the reference for building any new
component that participates in the pipeline.

Two design goals, in priority order:

1. **Correctness by identity, not timing.** Grasshopper provides no per-component timers:
   `GH_Document` keeps ONE schedule, a shorter delay replaces a longer one, and every
   pending callback fires just before the *next* solution however it starts. Any design
   that encodes event ordering in solve timing collapses as soon as two components are in
   flight. Events therefore travel as latched, sequence-numbered signals consumed exactly
   once — solves can coalesce, delay, or replay without reordering, duplicating, or
   dropping anything.
2. **Visible data flow.** A human watching the canvas can trace where data is: components
   advertise their lifecycle state and hold a visible delay at the end of each solve.
   Pacing is *cosmetic only* — correctness never depends on it.

---

## Signals

A `PhySignal` (Core, `src/Physalia.Core/Signals/`) is an immutable event:

| Field | Meaning |
|---|---|
| `Sequence` | Process-wide monotonic identity (`SignalSequencer`). Higher = happened later. **Sequence order is causal order**: a signal minted as a consequence of another always sorts after it. |
| `Outcome` | `Success` / `Failure` |
| `Payload` | The event's payload: data string on success, feedback string on failure |
| `SourceId` / `SourceName` | Emitting component, for tracing |
| `Timestamp` | UTC mint time, for tracing |

`PhySignal.Mint(...)` is the only way sequences are assigned.

**On the wire:** signals are **latched**, not pulsed. A component's Success/Fail Signal
output holds the minted signal until the next run (or Clear) replaces it. There are no
momentary pulses and no pulse-reset solves. Receivers track a per-input high-water mark
of consumed sequences: re-emitting the same signal on idle solves or document recomputes
can never re-fire anything.

**Wires are inspectable:** `GH_Signal.ToString()` renders
`#42 Success from Reasoner @ 14:03:21.187 — "payload preview"`, so a panel on any Signal
wire is a live ordering trace.

**Ephemeral:** signals never serialize (`GH_Signal.Write/Read` are no-ops). A reopened
file has signal-free wires; nothing fires on load.

**Native bool sources (Buttons/Toggles)** wire straight into Signal inputs. The cast does
NOT mint a signal (casts run every solve — a stuck-true Toggle would re-fire forever);
it captures the raw level as a sentinel, and the consuming component edge-detects with
its own per-input state, minting exactly one signal per false→true transition. A press
during an active run queues losslessly. The first observation of an input baselines it:
fresh, pasted, or reloaded components never fire off pre-existing wire state.

**Multiple sources** wire directly into one Signal input (list access) — no OR gates.
Each source's events have distinct sequences; the receiver consumes each exactly once,
in global sequence order. One caveat: a wire holds one latched signal per source, so two
events from the same source before consumption supersede (latest wins).

---

## The Four States

| State | Data out | Feedback out | Success Signal | Fail Signal | Caption |
|---|---|---|---|---|---|
| **Empty** | empty | empty | none | none | *(blank)* |
| **Active** | empty (cleared on entry) | empty | none (cleared on entry) | none | `Active…` |
| **SolveSuccess** | latched result | empty | latched (minted once) | none | `Success` |
| **SolveFailure** | empty | latched feedback | none | latched (minted once) | `Failed` |

- **Empty** — fresh on canvas, never ran, or manually cleared (right-click Clear).
- **Active** — entered when a signal is consumed. Stale outputs and outgoing signals
  blank immediately; the work runs (instant, deferred, or async); a visible delay
  (`SolveDelayMs = 500`, wall-clock honest) is held before the latch.
- **SolveSuccess / SolveFailure** — result latched, one signal minted. Downstream
  consumers fire on it exactly once.

## Solve Rhythm (routing components)

```
Solve A  (signal consumed):  EnterActive (clear outputs + outgoing signals)
                             PushSolve (side effects) → schedule read @1ms
                             (async components schedule it from their completion callback)
Solve B  (@1ms, repeats):    IsReadReady? no → retry @1ms (≤10) | yes → schedule latch @500ms
Solve C  (@500ms, honest):   ReadSolve → latch Data or Feedback → mint Success/Fail Signal
                             if signals arrived mid-run → schedule one follow-up solve
Solve D  (follow-up, only if needed): consume the oldest waiting signal → next run
```

There is no pulse-reset solve. The 500ms delay is enforced against the wall clock: if the
document flushes the schedule early (it flushes *everything* at the next solution), the
callback re-arms for the remainder instead of acting (`ScheduleStateSolve`).

---

## Two-Layer Architecture

### Layer 1 — `StatefulComponentBase : PhyBase`

`src/Physalia.GH/Components/StatefulComponentBase.cs`. Registers no parameters, has no
`SolveInstance`. Owns:

| Member | Role |
|---|---|
| `SolveState State`, `MessageForState`, `UpdateStateDisplay` | state machine + canvas caption |
| `SolveDelayMs = 500` | visible delay constant (may be user-exposed later) |
| `SuccessSignal` / `FailSignal` | latched outgoing signals |
| `LatchSuccess(payload, emitSignal=true, outcome=Success)` | mint + latch; `emitSignal:false` = quiet (no downstream fire); `outcome` override for pass-through truthfulness |
| `LatchFailure(payload, emitSignal=true)` | mirror |
| `EnterActive()` / `ResetToEmpty()` | transitions; both clear outgoing signals |
| `ObserveSignalInputs(da, indices…)` | call EVERY solve (even Active) for every Signal input; snapshots wire signals, edge-detects bool sentinels into a pending queue, applies the first-observation baseline |
| `HasUnconsumedSignals(…)` | peek — used post-latch to schedule the follow-up solve |
| `TryConsumeOldestSignal(idx, out s)` | one event, oldest first (routing components) |
| `ConsumeAllSignals(indices…)` | drain all, **global sequence order** (Recorder) |
| `ScheduleStateSolve(ms, action)` | wall-clock honest scheduling funnel; safe from background threads |
| `EmitSignal(da, idx, signal)` | emit helper; skips SetData when null so wires are genuinely empty |
| Clear menu (`ClearMenuText`), `ClearStateOutputs` / `OnCleared` hooks | Clear never resets consume-once bookkeeping (no replay) |
| `Write/Read` of `State`; `RestoreLatchedStateOnLoad` | signals never restored |

### Layer 2 — `RoutingComponentBase<TData> : StatefulComponentBase`

The standard contract for components that route a string result forward or back:

```
Inputs                                Outputs
  Data         (TData, index 0)         Data           (string — latched on success)
  …extras…                              Success Signal (latched, minted on success)
  Signal       (list, optional, last)   Feedback       (string — latched on failure)
                                        Fail Signal    (latched, minted on failure)
```

Subclasses implement `TryGetData` + `ReadSolve` (sync), plus `PushSolve`/`IsReadReady`
(two-pass) or `AutoScheduleRead => false` + `RequestReadPass()` from a completion
callback (async). `OnSolveTick` for per-solve inputs outside the lifecycle (Reasoner's
Cancel — a plain bool by design: it is a human abort, not a pipeline event).

Latched `DataOut`/`FeedbackOut` strings serialize; old pre-state-machine files infer
their state from whichever payload is non-blank.

### Current implementations

| Component | Shape | Notes |
|---|---|---|
| **Auditor** | sync | `ReadSolve` extracts + validates JSON |
| **PyTransmitter** | two-pass | pushes code into the linked Python component; `IsReadReady` waits for it |
| **Reasoner** | async | completion callback calls `RequestReadPass()`; Cancel aborts to Empty |
| **Recorder** | Layer 1 | dedicated signal inputs, identity-based turns (below) |
| **Feedback / FeedbackCollector** | Layer 1 | wireless signal transport (below) |

---

## Recorder: identity-based turns

Recorder consumes events from **three dedicated Signal inputs** — the turn type comes
from which input the signal arrived on, never from conversation parity:

| Input | Records | Text source |
|---|---|---|
| `Prompt Signal` | user turn | signal payload; empty payload (Button press) falls back to the `Prompt` string input |
| `Response Signal` (from Reasoner Success Signal) | assistant turn | Tool Calls list (priority), else payload |
| `Feedback Signal` (from Collector(s)) | user turn | payload |

- `ConsumeAllSignals` processes everything waiting in **global sequence order** — a
  feedback signal is always minted after the response that provoked it, so the assistant
  turn is recorded first even when both land in the same solve. **This is the fix for
  the original feedback-before-response race.**
- User-side text arriving when the last turn is already a user message **merges into
  that message** (`Conversation.MergeIntoLastUserMessage`) — providers require strict
  role alternation. Covers feedback after an API-failure (no assistant turn exists) and
  double prompts.
- Appends happen on the consume solve; only the latch + outgoing signal wait out the
  visible delay. Outgoing signal is minted **only for user turns** — assistant turns
  latch quietly, so `Reasoner.Success Signal → Recorder.Response Signal` plus
  `Recorder.Signal → Reasoner.Signal` cannot loop.
- A consumed signal with nothing recordable latches a quiet failure with a warning.
- Clear Conversation resets the log and outputs to Empty; consume-once bookkeeping is
  kept (no replay). Conversation is not serialized → Recorder always reopens Empty.

## Feedback / FeedbackCollector: wireless signal transport

- **Feedback** consumes upstream signals (typically a Fail Signal) exactly once and
  forwards each **as-is** — original sequence preserved — to every linked collector via
  `collector.Inject(signal)`. No level-triggered re-injection is possible.
- **FeedbackCollector** queues injections losslessly (lock-protected): a batch arriving
  in one solution aggregates; injections during an active run wait. After the visible
  delay it latches `Data` (newline-joined payloads, never wiped) and mints ONE outgoing
  signal per batch — fresh sequence, necessarily greater than every cause. The minted
  signal carries `Failure` if any injected signal was a failure (trace truthfulness;
  Recorder ignores outcome on its Feedback input).

## Canonical wiring (no OR gates, no bool trigger wires)

```
Panel(prompt) ──────────────► Recorder.Prompt
Button ─────────────────────► Recorder.Prompt Signal      (bool cast: one run per press)
Recorder.Signal ────────────► Reasoner.Signal
Recorder.Instructions ──────► Reasoner.Instructions
Reasoner.Data ──────────────► Auditor.Data
Reasoner.Success Signal ────► Recorder.Response Signal    AND ► Auditor.Signal
Auditor.Fail Signal ────────► Feedback.Signal
Feedback (grip) ~~~~~~~~~~~~► FeedbackCollector           (wireless)
Collector.Signal ───────────► Recorder.Feedback Signal
```

## Serialization Rules

- Layer 1 persists `State` only. Signals and consume-once bookkeeping never persist; a
  file saved mid-Active reopens Empty; Success/Failure reopen latched only when the
  payload is also serialized (`RestoreLatchedStateOnLoad` — true for routing components
  and FeedbackCollector, false for Recorder).
- Old files: stale `LastTrigger` keys are ignored; pre-state-machine files infer state
  from latched payloads.
- On load, wires are signal-free and inputs re-baseline on first observation → nothing
  ever fires on open.

## Building a New Pipeline Component

1. **Routes a string result forward/back on a signal?** Inherit
   `RoutingComponentBase<TData>` — the full contract is free.
2. **Signal-driven with bespoke outputs or quiet outcomes?** Inherit
   `StatefulComponentBase` (the Recorder pattern): `ObserveSignalInputs` every solve →
   consume → `EnterActive` → work → `ScheduleStateSolve(SolveDelayMs, …)` →
   `LatchSuccess/LatchFailure` (use `emitSignal:false` for quiet outcomes) → post-latch
   `HasUnconsumedSignals` follow-up check.
3. **No events, purely deterministic?** Plain `PhyBase` (the Composer pattern).

Rules that keep the system race-free — follow them in every new component:
- Never gate behavior on a bool edge between Physalia components; consume signals.
- Never wipe an output another component might still need to read; latch it.
- Never encode ordering in `ScheduleSolution` delays; ordering is sequence numbers.
- Observe signal inputs on **every** solve, including while Active.
