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

A `PhySignal` (Core, `src/Physalia.Core/Signals/`) is an immutable event **and the only
data carrier between pipeline components** — the payload rides the signal, so each hop
is one wire and the data can never get separated from the event announcing it:

| Field | Meaning |
|---|---|
| `Sequence` | Process-wide monotonic identity (`SignalSequencer`). Higher = happened later. **Sequence order is causal order**: a signal minted as a consequence of another always sorts after it. |
| `Outcome` | `Success` / `Failure` |
| `Payload` | The event's payload: result string on success, feedback string on failure |
| `SourceId` / `SourceName` | Emitting component, for tracing |
| `Timestamp` | UTC mint time, for tracing |

`PhySignal.Mint(...)` is the only way sequences are assigned.

**On the wire:** signals are **latched**, not pulsed. A component's Success/Fail Signal
output holds the minted signal until the next run (or Clear) replaces it. There are no
momentary pulses and no pulse-reset solves. Receivers track a per-input high-water mark
of consumed sequences: re-emitting the same signal on idle solves or document recomputes
can never re-fire anything.

**Wires are inspectable:** `GH_Signal.ToString()` renders
`#42 Success from LLM Call @ 14:03:21.187 — "payload preview"`, so a panel on any Signal
wire is a live ordering trace.

**Interop / escape hatches:**
- A Signal **casts to text** (the payload), so a signal wire plugs straight into any
  native GH text input.
- **Deconstruct Signal** (passive, never consumes) breaks a signal into
  Sequence / Success / Payload / Source / Time.
- **Construct Signal** mints a signal from a text payload + trigger — the manual entry
  point for running any pipeline component standalone (e.g. Panel of JSON → Schema Validator).
- A latched signal casts to bool **true** (a level, not a pulse) for native bool inputs.

**Ephemeral:** signals never serialize (`GH_Signal.Write/Read` are no-ops). A reopened
file has signal-free wires; nothing fires on load and every component reopens Empty.

**Signal inputs accept only signals.** A bare bool source (Button/Toggle) carries no
payload, so it does **not** cast to a signal — wiring one into a Signal input is a **hard
error**, the same as wiring text, numbers, or geometry. `ObserveSignalInputs` catches this
by inspecting the source goo directly (it keeps its original type even after a failed cast),
so a genuinely foreign source is told apart from a benign null/empty wire and only the
foreign one fails loudly. The first observation of an input baselines it: fresh, pasted, or
reloaded components never fire off pre-existing wire state.

**Manual runs** go through **Construct Signal**, whose dedicated native **Boolean Trigger**
input (edge-detected by `ObserveButtonPress` — one mint per false→true press, nothing on
load/paste) mints a payload-carrying signal. That is the one sanctioned place a Button drives
the pipeline; everywhere else the wire is signal-to-signal.

**Multiple sources** wire directly into one Signal input (list access) — no OR gates.
Each source's events have distinct sequences; the receiver consumes each exactly once,
in global sequence order. One caveat: a wire holds one latched signal per source, so two
events from the same source before consumption supersede (latest wins).

---

## The Four States

| State | Success Signal | Fail Signal | Caption |
|---|---|---|---|
| **Empty** | none | none | *(blank)* |
| **Active** | none (cleared on entry) | none | `Active…` |
| **SolveSuccess** | latched (minted once; payload = result) | none | `Success` |
| **SolveFailure** | none | latched (minted once; payload = feedback) | `Failed` |

- **Empty** — fresh on canvas, never ran, manually cleared (right-click Clear), or
  freshly loaded from a file (nothing persists).
- **Active** — entered when a signal is consumed. Outgoing signals blank immediately;
  the work runs (instant, deferred, or async); a visible delay (`SolveDelayMs = 500`,
  wall-clock honest) is held before the latch.
- **SolveSuccess / SolveFailure** — result latched inside one minted signal. Downstream
  consumers fire on it exactly once; humans read it via a panel, the text cast, or
  Deconstruct Signal.

## Solve Rhythm (routing components)

```
Solve A  (signal consumed):  EnterActive (clear outgoing signals)
                             PushSolve (side effects) → schedule read @1ms
                             (async components schedule it from their completion callback)
Solve B  (@1ms, repeats):    IsReadReady? no → retry @1ms (≤10) | yes → schedule latch @500ms
Solve C  (@500ms, honest):   ReadSolve → mint + latch Success Signal or Fail Signal
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
| `SuccessSignal` / `FailSignal` | latched outgoing signals (the payload lives inside) |
| `LatchSuccess(payload, emitSignal=true, outcome=Success)` | mint + latch; `emitSignal:false` = quiet (no downstream fire); `outcome` override for pass-through truthfulness |
| `LatchFailure(payload, emitSignal=true)` | mirror |
| `EnterActive()` / `ResetToEmpty()` | transitions; both clear outgoing signals |
| `ObserveSignalInputs(da, indices…)` | call EVERY solve (even Active) for every Signal input; snapshots wire signals, applies the first-observation baseline, errors on a non-signal source |
| `ObserveButtonPress(da, boolIndex)` | edge-detect a native Boolean trigger input; true once per false→true press (first observation baselines). The sanctioned Button path — Construct Signal |
| `HasUnconsumedSignals(…)` | peek — used post-latch to schedule the follow-up solve |
| `TryConsumeOldestSignal(idx, out s)` | one event, oldest first (routing components) |
| `ConsumeAllSignals(indices…)` | drain all, **global sequence order** (Conversation Log) |
| `ScheduleStateSolve(ms, action)` | wall-clock honest scheduling funnel; safe from background threads |
| `EmitSignal(da, idx, signal)` | emit helper; skips SetData when null so wires are genuinely empty |
| Clear menu (`ClearMenuText`), `ClearStateOutputs` (virtual) / `OnCleared` hooks | Clear never resets consume-once bookkeeping (no replay) |

**Nothing persists.** The base has no `Write`/`Read`: state, signals, and consume-once
bookkeeping are all session-only, so every component reopens Empty and re-baselines its
inputs on first observation — nothing ever fires on file open.

### Layer 2 — `RoutingComponentBase<TData> : StatefulComponentBase`

The standard contract for components that route a result forward or back. **One wire per
hop**: the consumed signal carries the working data in; the minted signal carries the
result (or feedback) out.

```
Inputs                                 Outputs
  …subclass extras…                      Success Signal (latched; payload = result)
  Signal  (list, optional, last)         Fail Signal    (latched; payload = feedback)
```

Subclasses implement `TryGetData(PhySignal signal, da, out TData)` — most take
`signal.Payload`; components whose context arrives on a typed input read that instead
(LLM Call reads Instructions and ignores the payload) — plus `ReadSolve` (sync), plus
`PushSolve`/`IsReadReady` (two-pass) or `AutoScheduleRead => false` + `RequestReadPass()`
from a completion callback (async). `OnSolveTick` for per-solve inputs outside the
lifecycle (LLM Call's Cancel — a plain bool by design: it is a human abort, not a
pipeline event).

### Current implementations

| Component | Shape | Data in |
|---|---|---|
| **Schema Validator** | sync | signal payload (raw LLM text); Schema input for validation |
| **Schema Translator** | sync | signal payload (PhySchema JSON); Schema In input for validation |
| **PyTransmitter** | two-pass | signal payload (PythonComponent JSON); pushes into the linked Python component |
| **LLM Call** | async | Instructions input (typed); signal payload ignored |
| **Conversation Log** | Layer 1 | dedicated signal inputs, identity-based turns (below) |
| **Prompter** | Layer 1 (source) | chat UI; each Shift+Enter submit mints one Prompt Signal (payload = prompt text); upper panel displays the wired Conversation Log's active conversation |
| **Feedback / FeedbackCollector** | Layer 1 | wireless signal transport (below) |
| **Construct / Deconstruct Signal** | Layer 1 / plain | manual mint / passive inspect |

---

## Conversation Log: identity-based turns

Conversation Log consumes events from **three dedicated Signal inputs** — the turn type comes
from which input the signal arrived on, never from conversation parity:

| Input | Records | Text source |
|---|---|---|
| `Prompt Signal` | user turn | signal payload (the prompt text), from Prompter or Construct Signal; an empty payload warns and is dropped |
| `Response Signal` (from LLM Call Success Signal) | assistant turn | Tool Calls list (priority), else payload |
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
  latch quietly, so `LLM Call.Success Signal → Conversation Log.Response Signal` plus
  `Conversation Log.Signal → LLM Call.Signal` cannot loop.
- A consumed signal with nothing recordable latches a quiet failure with a warning.
- Clear Conversation resets the log and outputs to Empty; consume-once bookkeeping is
  kept (no replay). Conversation is not serialized → Conversation Log always reopens Empty.

## Feedback / FeedbackCollector: wireless signal transport

- **Feedback** consumes upstream signals (typically a Fail Signal) exactly once and
  forwards each **as-is** — original sequence preserved — to every linked collector via
  `collector.Inject(signal)`. No level-triggered re-injection is possible.
- **FeedbackCollector** queues injections losslessly (lock-protected): a batch arriving
  in one solution aggregates; injections during an active run wait. After the visible
  delay it mints ONE outgoing signal per batch — payload = newline-joined feedback,
  fresh sequence necessarily greater than every cause. The minted signal carries
  `Failure` if any injected signal was a failure (trace truthfulness; Conversation Log ignores
  outcome on its Feedback input).

## Signals utilities

- **Construct Signal** (`Physalia > Signals`): Payload text + Failure flag + trigger →
  one minted signal per consumed trigger, latched immediately (a source, not a hop —
  no visible delay). The trigger's own payload is ignored.
- **Deconstruct Signal**: Signal → Sequence / Success / Payload / Source / Time.
  Passive: it never consumes, so it can tap any wire without disturbing the
  consume-once bookkeeping of real receivers.

## Canonical wiring (one wire per hop; no OR gates, no bool trigger wires)

```
Prompter.Prompt Signal ────► Conversation Log.Prompt Signal       (payload = prompt text; Shift+Enter mints)
  (manual alternative: Panel ► Construct Signal.Payload, Button ► Construct Signal.Trigger,
   Construct Signal.Signal ► Conversation Log.Prompt Signal)
Conversation Log.Signal ───────────► LLM Call.Signal
Conversation Log.Instructions ─────► LLM Call.Instructions
LLM Call.Success Signal ───► Schema Validator.Signal    AND ► Conversation Log.Response Signal
Schema Validator.Success Signal ────► PyTransmitter.Signal         (payload = validated JSON)
Schema Validator.Fail Signal ───────► Feedback.Signal
PyTransmitter.Fail Signal ─► Feedback.Signal
Feedback (grip) ~~~~~~~~~~~► FeedbackCollector            (wireless)
Collector.Signal ──────────► Conversation Log.Feedback Signal
```

There is no separate data wire between pipeline components — the response text, the
validated JSON, and the feedback all travel as signal payloads.

### Iterative-canvas loop guardrails (Component Transmitter variant)

When the pipeline places graphs on the canvas (Schema Validator → Component Transmitter →
Runtime Health Check, Fail Signals looping back through Feedback/FeedbackCollector), three
extra components are part of the canonical wiring, not optional extras:

- **Canvas State grounder → Conversation Log.Grounding.** Guarantees the model sees the
  live canvas + a fresh patch-base checksum at every inference (the Conversation Log
  re-exports synchronously at latch). Without it the model edits blind and its patch
  bases go stale; the Conversation Log warns when a feedback turn latches without one.
  (Error feedback also carries a fresh checksum line as a belt-and-braces measure, but
  only the grounding carries the canvas itself.)
- **Signal Limiter on the Fail path** (e.g. limit 8) between the feedback sources and the
  Feedback component. An unbounded Fail cycle is an unbounded token spend when the model
  cannot converge. Route "Over Limit" to a Panel (or nothing) so the loop parks visibly
  instead of spinning.
- **Stall Guard between the Feedback Collector and the Conversation Log's Feedback Signal
  input** (Stall Limit, default 3; 0 disables). The Signal Limiter caps *total* rounds;
  the Stall Guard caps *identical* rounds. It fingerprints each failure payload (exact
  identity after stripping the volatile checksum line) and, when the same failure text
  arrives for the Nth consecutive time, passes it once more with an escalation preamble
  telling the model to stop patching and explain the blocker to the human in prose (the
  Detect JSON gate then parks the prose reply naturally); identical failures beyond the
  limit are not re-emitted at all — the guard captions `STALLED (Nx)` and latches the
  suppressed signal on its Stalled output for optional human-facing wiring. Any different
  failure, any success signal, or a menu Clear resets the streak, so the guard self-heals
  the moment the loop actually moves. Success signals (tool results, content blocks) pass
  through untouched — the original signal object is re-emitted, never re-minted.

Two related dials on the Runtime Health Check: **Fail on Warnings** (a context-menu
toggle, default on — a menu item rather than an input, because an input registered before
the base-appended Signal shifts the param layout of saved documents) lets a rig treat a
warnings-only scan as informational (routes Success with a remark) instead of
feeding benign warnings into the loop; and the data-flow section of its report samples
actual port values (point coordinates, curve closed/planar flags, `(only N distinct)`
duplicate detection, branch paths on treed ports), so the model diagnoses from the data
that exists instead of hypothesizing from item counts alone.

**Geometry Report — the self-review turn.** A graph that solves cleanly can still be
semantically wrong (parts floating apart, geometry buried inside other geometry), and
nothing errors, so no feedback fires. The Geometry Report closes that gap without images:
wired after the Runtime Health Check's Success Signal (so it only measures healthy
graphs), it accumulates watched GUIDs like the health check and broadcasts a text digest —
per-component bounding boxes from actual output geometry, whole-model bbox, disjoint-group
detection with gap distances, neutral containment facts, and a fresh base checksum. Routed
through Feedback/Collector into the Conversation Log it becomes a self-review turn: the
model compares the measured facts against its intent and either replies in prose
("matches intent" — the Detect JSON gate parks the loop) or submits a corrective ghpatch
(another placement round, bounded by the Signal Limiter and Stall Guard as usual).

## Tool calling: LLM Call → Router → tool nodes → Conversation Log

The provider contract (Anthropic/OpenAI/Gemini) is strict: an assistant turn that
contains N `tool_use` blocks **must** be followed by exactly **one** user turn carrying a
`tool_result` for **every** one of those ids, before any further assistant turn. The
pipeline preserves this by identity, not timing.

```
LLM Call.Tool Calls (aux) ──► Router.Tool Calls          (assistant turn: text + tool_use blocks)
Router.<tool> output ────────► ToolNode.Signal           (dispatched call(s) as tool_use blocks)
Router.Feedback ─► Feedback ~► FeedbackCollector ─► Conversation Log.Tool Signal
ToolNode.Result ─► Feedback ~► FeedbackCollector ─► Router.Results
```

- **LLM Call** routes a tool-call response on its **aux** `Tool Calls` output (not Success):
  one assistant turn = optional `TextContent` + one `ToolCallContent` per call. A plain
  answer routes on Success as usual.
- **Router** has one variable output per tool (auto-named from the wired tool node's
  `Tool Definition`; see the component). It:
  1. forwards the whole assistant turn on its fixed **Feedback** output so the Conversation Log
     logs the request (recorded as a *quiet* assistant turn — must not re-fire the LLM Call);
  2. **groups calls by target output** and dispatches all calls for one tool as a **single**
     signal carrying multiple `ToolCallContent` blocks — a single output holds only one
     latched signal, so parallel calls to the *same* tool cannot ride separate signals;
  3. **aggregates results**: holds every dispatched `tool_use` id in `_pendingToolUseIds`,
     accumulates returning `ToolResultContent` blocks (matched by `tool_use_id`), and
     forwards **one** combined Feedback signal only once the whole set is satisfied
     (`ResultsReady()`). That becomes the single user turn the provider requires, firing the
     LLM Call exactly once. A call with no matching output is answered with a synthetic
     `is_error` result so the round can still complete.

- **Tool nodes inherit `ToolComponentBase`** (`Components/Tools/ToolComponentBase.cs`), which
  owns the whole contract: it advertises `Definition` on the Tool output, observes/consumes the
  Signal input, and — crucially — handles the multi-call case. The dispatched signal may carry
  **more than one** `ToolCallContent` (the model called the tool several times in one turn), so
  the base runs `ExecuteCall` **once per call** and emits **one** result signal whose
  `ContentBlocks` hold a `ToolResultContent` per call, each echoing that call's `Id`. Answering
  only the first call would strand the others' ids as permanently pending and the round would
  never complete. A subclass supplies only: `Definition`, optional `RegisterAdditionalInputs` +
  `OnSolveTick` (cache per-solve context like a wired catalog), and `ExecuteCall(call) →
  ToolCallResult` (`Ok`/`Error`). `ComponentSearch` is the reference implementation.

- The body of a tool result rides as `ToolResultContent.Content` inside `ContentBlocks` — the
  Router collects from blocks, not the payload, so a result returned as payload-only text
  (no `ToolResultContent`) is **not** seen. `ToolComponentBase` always wraps output correctly;
  don't bypass it.

## Serialization Rules

- **Nothing in the lifecycle persists.** No state, no signals, no payloads, no
  consume-once bookkeeping. Every component reopens Empty with a blank caption.
- Component-specific domain settings still persist where they always did (PyTransmitter
  link GUID, Feedback collector links). API keys never serialize.
- On load, wires are signal-free and inputs re-baseline on first observation → nothing
  ever fires on open. Stale keys from older files (`State`, `DataOut`, `FeedbackOut`,
  `LastTrigger`) are ignored harmlessly.

## Building a New Pipeline Component

1. **Routes a result forward/back on a signal?** Inherit `RoutingComponentBase<TData>`
   — the full contract is free. Take the working data from `signal.Payload` unless it
   genuinely needs a typed input (the LLM Call pattern).
2. **Signal-driven with bespoke outputs or quiet outcomes?** Inherit
   `StatefulComponentBase` (the Conversation Log pattern): `ObserveSignalInputs` every solve →
   consume → `EnterActive` → work → `ScheduleStateSolve(SolveDelayMs, …)` →
   `LatchSuccess/LatchFailure` (use `emitSignal:false` for quiet outcomes) → post-latch
   `HasUnconsumedSignals` follow-up check.
3. **No events, purely deterministic?** Plain `PhyBase` (the System Prompt / Deconstruct
   Signal pattern).

Rules that keep the system race-free — follow them in every new component:
- Never gate behavior on a bool edge between Physalia components; consume signals.
- The signal payload IS the data — never add a parallel string wire that can disagree
  with the event that announced it.
- Never encode ordering in `ScheduleSolution` delays; ordering is sequence numbers.
- Observe signal inputs on **every** solve, including while Active.
- Never serialize lifecycle state; components must reopen Empty.
