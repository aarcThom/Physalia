# Guardrails — Research

> Research deliverable (2026-06-27). Catalog of deterministic (non-LLM) "gate" components
> worth building for the Physalia agent pipeline. Not yet implemented — this is the spec.

## What a gate is

A **gate** is a deterministic `RoutingComponentBase<TData>` subclass that consumes an incoming
signal, inspects the payload (and/or document state), and routes it forward on **Success Signal (0)**
or back on **Fail Signal (1)** — **without an LLM call**. The Fail payload is feedback that flows
(Feedback → FeedbackCollector → Conversation Log) into the next prompt. This is the industry
"Generate → Validate (deterministic) → feed exact errors back → Retry" loop rendered as canvas nodes.

Why gates matter here: the LLM Call is the only expensive node. Every gate placed before a LLM Call
re-entry, or after a LLM Call (catching bad output before a Transmitter mutates the doc), saves a full
forward pass. The pattern is already proven twice — **Schema Validator** (JSON + schema) and **Canvas Observation**
(doc-state). Most gates are synchronous: empty `PushSolve`, all logic in `ReadSolve` returning
`RoutingResult.Ok`/`.Fail` (the Schema Validator shape). Stateful counters (retry, dedup, budget) instead
follow the **SignalLimiter** shape — subclass `StatefulComponentBase`, accumulate per-session, add a
Reset Boolean — because they route the *same* signal by a running count rather than minting feedback.

## Already covered by existing components — do NOT build

The existing pipeline already implements the obvious deterministic checks. Building these again
would be redundant:

- **JSON well-formedness + schema validation → Schema Validator.** `SchemaValidator.Validate` runs
  `JsonDocument.Parse` and fails on malformed JSON before schema checking. (Numeric/string bounds can
  also be enforced here via JSON Schema `minimum`/`maximum`/`pattern` when the output is JSON.)
- **Python syntax + runtime errors → PyTransmitter.** It pushes the code into the linked Script
  component, waits for it to re-solve, reads `GhPythonBridge.GetErrors`, and routes them on Fail.
- **Placed-graph errors / dead components → Canvas Observation and ComponentTransmitter.**
- **Attempt/retry capping → SignalLimiter.** It already routes the first N signals to Within Limit
  and the rest to Over Limit with a Reset — i.e. it *is* the Retry Limiter/Counter. At most it wants
  a thin relabel, not a new component.

So the valuable new gates are the ones that check something **nothing currently checks**.

## Catalog (prioritized)

### Priority 1 — highest leverage

1. **Retry Limiter** (Counter / Loop Breaker) — ⚠️ **already covered by SignalLimiter**, which routes
   the first N signals to Within Limit and the rest to Over Limit with a Reset. Loop safety is
   essential, but it is achieved by wiring the loop's feedback through the existing SignalLimiter — not
   a new component. At most, add a thin relabelled variant (Continue/Halted + a terminal payload) if
   the UX matters. **Not a new build.**
2. **JSON Well-Formedness Gate** — *trivial; reuses `JsonExtractor`.* ⚠️ **Largely redundant with the
   Schema Validator.** `SchemaValidator.Validate` already does `JsonDocument.Parse` and Fails with
   `Invalid JSON: …` on a parse error, so whenever a schema is wired (the normal case — the System Prompt
   always feeds one) the Schema Validator already rejects malformed JSON. A standalone gate only adds value in
   the Schema Validator's `schema == ""` passthrough branch (prototyping with no schema yet). **Do not
   prioritize.** If wanted, it's a few lines, but it is not one of the first gates to build.
   > **Note (2026-07-03):** this rejection covers *well-formedness* only. The inverse-polarity
   > **JSON Presence gate** — pass anything containing attempted JSON (even malformed, so the
   > Schema Validator's correction loop still fires) and route pure conversation to Fail as a quiet
   > switch — is NOT redundant with anything, and was built as **Detect JSON**
   > (`Components/ControlFlow/DetectJson.cs`, heuristic in `Core/Validation/JsonDetector.cs`).
3. **Python Syntax Gate** — ⚠️ **already covered by PyTransmitter**, which pushes the code into the
   linked Script component, waits for it to re-solve, reads `GhPythonBridge.GetErrors`, and routes the
   syntax/runtime errors on Fail. A pre-execution gate only matters if you specifically want to avoid
   pushing into the linked component first — but that component is the intended target, and the errors
   are already routed deterministically. **Not a new build.** (A true static *pre-flight* PyValidator —
   e.g. an allow/deny safety scan, see #6 — is the part with non-redundant value.)

### Priority 2 — broadly useful, mostly trivial

4. **Regex / Pattern Gate** — `Regex.IsMatch`; optional `Invert`, capture-group extraction. Force a
   LLM Call to emit a marker (e.g. `^DONE:` to terminate a loop, or a required fenced block).
5. **Length / String-Property Gate** — non-empty, min/max length, contains / not-contains. Guard
   against empty/truncated or ballooned responses before a Transmitter. Reuses `StringHelpers`.
6. **Allow/Deny Content Filter** — denylist (and optional allowlist) over the payload; a security
   specialization of the regex gate. Block dangerous generated Python (`os.system`, `subprocess`,
   `eval`, file-delete) before PyTransmitter executes it. Denylist loadable from a `.deny` file.
7. **Numeric Range / Parse Gate** — `double.TryParse` (optionally a JSON field via a path), then a
   `[min, max]` range check. Enforce a confidence score / count / dimension is a real number in bounds.

### Priority 3 — valuable, moderate

8. **Deduplication Gate** — *SignalLimiter shape.* Normalize + hash the payload; route first-seen to
   **New**, repeats to **Duplicate**. Breaks the "model returns the same wrong answer twice" livelock
   the Retry Limiter would only count down. (Note: the old implicit SHA-256 change-detection was
   removed from the lifecycle; this is fine as an explicit, user-placed gate.)
9. **Token / Length Budget Gate** (Meter) — *SignalLimiter shape.* Accumulate estimated tokens
   (`ITokenEstimator` / `TokenEstimationHelpers`) against a `Budget`; route **Within** / **Over Budget**.
   Caps total spend across all retries — critical for BYOK. (The spec's planned *Meter*.)
10. **Equality / Diff Gate** — route by whether the payload equals a reference or the previous payload;
    optionally emit a line-diff as feedback. Convergence detection (stop when output stabilizes).

### Priority 4 — domain-specific

11. **Geometry-Valid Gate (Rhino)** — *moderate; Canvas Observation's `IsReadReady` deferral as template.* After
    placement, assert produced geometry is valid via RhinoCommon (`IsValidWithLog`, `Brep.IsValid`,
    `Mesh.IsManifold`), routing the validity log back as feedback. The deepest correctness gate — and
    the most Physalia-unique (Canvas Observation catches GH runtime errors; a Brep can be error-free yet invalid).
12. **File-Exists / Path Gate** — *trivial; `System.IO`.* Validate a path exists + has an allowed
    extension before Deserializer / Component Catalog / Image Sources reads it.
13. **C# / Roslyn Compile Gate** — *hard; defer.* Only relevant if Physalia generates C#; the Python
    gate is the one that matters today.

Other quick ideas: encoding/UTF gate, list item-count gate, a forward-only whitespace/markdown-fence
"cleaner" (reuses `JsonExtractor.ExtractJson`), schema-presence fail-fast gate.

## Recommended first three

Chosen for being genuinely new — each checks something **no existing component checks**. (The earlier
draft listed a JSON well-formedness gate, a Python syntax gate, and a Retry Limiter; all three were
dropped as redundant with the Schema Validator, PyTransmitter, and SignalLimiter respectively — see the
"Already covered" section.)

1. **Geometry-Valid Gate** (#11) — the deepest, most Physalia-unique correctness check. The Canvas Observation
   catches GH *runtime* errors, but a Brep/Mesh can be error-free yet geometrically invalid
   (non-manifold, bad tolerances); nothing checks `IsValidWithLog`. Routes the validity log back as
   feedback. *Moderate (Canvas Observation's `IsReadReady` deferral is the template).*
2. **Allow/Deny Content Filter** (#6) — a real safety rail: block dangerous generated Python
   (`os.system`, `subprocess`, file-delete) **before** PyTransmitter executes it. Non-redundant —
   PyTransmitter routes *errors*, but harmful code that runs successfully produces no error. *Trivial.*
3. **Token Budget Meter** (#9) — caps cumulative token spend across all retries; the *resource* cap
   complementing SignalLimiter's *attempt* cap. Nothing tracks spend today. Critical for BYOK.
   *Moderate.*

Strong next: **Deduplication Gate** (#8) — detects the "model returns the same wrong answer twice"
livelock that SignalLimiter only counts down on. Then **Regex/Marker** (#4) for loop self-termination.

## Key files for implementation
- Base: `src/Physalia.GH/Components/RoutingComponentBase.cs` (sync gate template — `RoutingResult.Ok`/`.Fail`).
- Counter template: `src/Physalia.GH/Components/Utility/SignalLimiter.cs` (+ `StatefulComponentBase`).
- Existing gates to copy: `Components/Guardrails/SchemaValidator.cs`, `Components/Guardrails/CanvasObservation.cs`.
- Reusable Core: `Common/StringHelpers.cs`, `Validation/JsonExtractor.cs` (+ `SchemaValidator`),
  `Tokens/ITokenEstimator.cs` / `TokenEstimationHelpers.cs`, `Generation/GhPythonBridge.cs`.
- Specs for planned versions: `planning/physalia-primitives.md` (Counter, Meter, PyValidator).

## Sources
- "Stop Blaming the LLM: JSON Schema Is the Cheapest Fix for Flaky AI Agents" (Medium).
- Promptfoo deterministic metrics; FutureAGI deterministic eval; Rulebricks deterministic guardrails;
  AWS "AI Agent Guardrails: Rules That LLMs Cannot Bypass"; langgraph-deterministic-validation (GitHub).
