# Deterministic Gates — Research

> Research deliverable (2026-06-27). Catalog of deterministic (non-LLM) "gate" components
> worth building for the Physalia agent pipeline. Not yet implemented — this is the spec.

## What a gate is

A **gate** is a deterministic `RoutingComponentBase<TData>` subclass that consumes an incoming
signal, inspects the payload (and/or document state), and routes it forward on **Success Signal (0)**
or back on **Fail Signal (1)** — **without an LLM call**. The Fail payload is feedback that flows
(Feedback → FeedbackCollector → Recorder) into the next prompt. This is the industry
"Generate → Validate (deterministic) → feed exact errors back → Retry" loop rendered as canvas nodes.

Why gates matter here: the Reasoner is the only expensive node. Every gate placed before a Reasoner
re-entry, or after a Reasoner (catching bad output before a Transmitter mutates the doc), saves a full
forward pass. The pattern is already proven twice — **Auditor** (JSON + schema) and **Observer**
(doc-state). Most gates are synchronous: empty `PushSolve`, all logic in `ReadSolve` returning
`RoutingResult.Ok`/`.Fail` (the Auditor shape). Stateful counters (retry, dedup, budget) instead
follow the **SignalLimiter** shape — subclass `StatefulComponentBase`, accumulate per-session, add a
Reset Boolean — because they route the *same* signal by a running count rather than minting feedback.

## Catalog (prioritized)

### Priority 1 — highest leverage

1. **Retry Limiter** (Counter / Loop Breaker) — *trivial; SignalLimiter shape.* Cap self-correction
   round-trips so a failing loop terminates. Inputs: `Max Attempts` (default 3), `Reset`. Routes the
   signal to **Continue** while at/under the limit, to **Halted** (terminal message) once exceeded.
   The single most important safety gate — without it every agent loop is an unbounded token sink.
   (This is the spec's planned *Counter*.)
2. **JSON Well-Formedness Gate** — *trivial; reuses `JsonExtractor`.* Cheap structural pre-check that
   the payload parses as JSON (`JsonExtractor.ExtractJson` + `JsonNode.Parse`), distinct from the
   Auditor's heavier schema validation. Forward pretty-printed JSON, or Fail with the parse error +
   position. Use as a fast first stage before a schema Auditor, or when there is no schema yet.
3. **Python Syntax Gate** — *moderate; reuses `GhPythonBridge`/`CodeChecker`.* Catch Python syntax /
   undefined-name errors before PyTransmitter pushes code into a live Script component and mutates the
   doc. The first concrete slice of the planned **PyValidator**. Report pyflakes-style messages keyed
   by stable codes (E999/F821/F401), optionally injecting input-name stubs to suppress false F821s.

### Priority 2 — broadly useful, mostly trivial

4. **Regex / Pattern Gate** — `Regex.IsMatch`; optional `Invert`, capture-group extraction. Force a
   Reasoner to emit a marker (e.g. `^DONE:` to terminate a loop, or a required fenced block).
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

11. **Geometry-Valid Gate (Rhino)** — *moderate; Observer's `IsReadReady` deferral as template.* After
    placement, assert produced geometry is valid via RhinoCommon (`IsValidWithLog`, `Brep.IsValid`,
    `Mesh.IsManifold`), routing the validity log back as feedback. The deepest correctness gate — and
    the most Physalia-unique (Observer catches GH runtime errors; a Brep can be error-free yet invalid).
12. **File-Exists / Path Gate** — *trivial; `System.IO`.* Validate a path exists + has an allowed
    extension before Deserializer / Library / Image Gatherer reads it.
13. **C# / Roslyn Compile Gate** — *hard; defer.* Only relevant if Physalia generates C#; the Python
    gate is the one that matters today.

Other quick ideas: encoding/UTF gate, list item-count gate, a forward-only whitespace/markdown-fence
"cleaner" (reuses `JsonExtractor.ExtractJson`), schema-presence fail-fast gate.

## Recommended first three

1. **Retry Limiter** — non-negotiable loop safety; SignalLimiter is nearly the implementation.
2. **JSON Well-Formedness Gate** — pure reuse of `JsonExtractor`; cheap structural check.
3. **Python Syntax Gate** — highest-leverage domain gate; stops bad Python before it mutates the doc.

These cover the count-based, content-based, and execution-based families. Regex (#4) and Meter (#9)
are the natural next additions.

## Key files for implementation
- Base: `src/Physalia.GH/Components/RoutingComponentBase.cs` (sync gate template — `RoutingResult.Ok`/`.Fail`).
- Counter template: `src/Physalia.GH/Components/Utility/SignalLimiter.cs` (+ `StatefulComponentBase`).
- Existing gates to copy: `Components/Core/Auditor.cs`, `Components/Regulators/Observer.cs`.
- Reusable Core: `Common/StringHelpers.cs`, `Validation/JsonExtractor.cs` (+ `SchemaValidator`),
  `Tokens/ITokenEstimator.cs` / `TokenEstimationHelpers.cs`, `Generation/GhPythonBridge.cs`.
- Specs for planned versions: `planning/physalia-primitives.md` (Counter, Meter, PyValidator).

## Sources
- "Stop Blaming the LLM: JSON Schema Is the Cheapest Fix for Flaky AI Agents" (Medium).
- Promptfoo deterministic metrics; FutureAGI deterministic eval; Rulebricks deterministic guardrails;
  AWS "AI Agent Guardrails: Rules That LLMs Cannot Bypass"; langgraph-deterministic-validation (GitHub).
