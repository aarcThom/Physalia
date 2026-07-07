---
name: detect-json-gate
description: Detect JSON presence gate (LLM Call→Schema Validator) routes chat replies to Fail quietly; malformed JSON still passes to Schema Validator
metadata: 
  node_type: memory
  type: project
  originSessionId: a585e471-72d4-4dbc-91c6-8dd9058ed1c6
---

**Detect JSON gate** (2026-07-03, branch feat/memory-tool): new Control Flow-tab component `src/Physalia.GH/Components/ControlFlow/DetectJson.cs` (`RoutingComponentBase<string>`, Component Resolver shape, GUID 85E51782-BA18-4B96-9488-B574950F2963) + pure heuristic `src/Physalia.Core/Validation/JsonDetector.ContainsJson` + 15 xUnit tests.

Purpose: sits between LLM Call and Schema Validator so casual chat ("hello") no longer fires "Your previous response failed validation" feedback. It is a **presence/intent** gate, NOT well-formedness (the gate `planning/deterministic-gates.md` rejected — doc amended with a note):
- Any attempted JSON, even malformed/truncated → **Success** (Schema Validator still judges it; correction loop intact).
- No JSON at all → **Fail**, payload = raw chat text (pure demultiplexer; fail left unwired = quiet switch), runtime message level **Remark** (canvas never goes orange for chat).

Heuristic (bias: when in doubt, pass through): ```json fence anywhere, OR trimmed text starts with `{`/`[`, OR quoted-key-colon regex (`"key":`) after the first `{`/`[`. `{placeholder}` prose → false.

Deviations from Schema Validator: `TryGetData` accepts blank payload (blank response routes to Fail rather than dropping with a warning).

Builds clean, 191 tests green. **Live Rhino test pending** (wire LLM Call Success → Detect JSON → Schema Validator; "hello" should dead-end quietly). Related: [[signal-carrier-discipline]].
