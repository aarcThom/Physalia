---
name: single-signal-output-rework
description: "RoutingComponentBase gained optional Fail output (HasFailOutput) + quiet failures; Detect JSON, Geometry Report, Stall Guard collapsed to one output"
metadata: 
  node_type: memory
  type: project
  originSessionId: 306541bc-0ef9-4671-8f4e-ec93e40c9e78
---

2026-07-10 rework: components whose two signal outputs carried the same/duplicate signal now expose a single output.

Base affordances (`RoutingComponentBase`):
- `protected virtual bool HasFailOutput => true` — override false → single output "Signal"/"S" at index 0; `Emit` sends `SuccessSignal ?? FailSignal` there (failures still fire, keeping loops alive; the signal's `SignalOutcome` records how the run ended). Flipping it on a shipped component shifts saved-doc output layouts.
- `OutFailSignal` is now a property (`HasFailOutput ? 1 : -1`), plus `FirstAdditionalOutputIndex` (2 or 1) for computing `AuxOutputIndex`.
- `RoutingResult.Fail(..., emitSignal: false)` — quiet failure: state/caption update, nothing minted, event dead-ends inside the component (mirrors `LatchFailure(emitSignal:false)`).

Converted components:
- **Detect JSON**: `HasFailOutput => false`; plain conversation = quiet Fail (dead-ends inside; Failed caption + Remark).
- **Geometry Report**: `HasFailOutput => false`; `Broadcast` simplified to `Ok(report)`; doc-unavailable Fail rides the single wire.
- **Geometry Observation**: `HasFailOutput => false`; keeps `Broadcast` (signal is caller-minted to carry the snapshot image content block — single-output base emits it once); capture failures ride the single wire.
- **Stall Guard** (direct StatefulComponentBase subclass): dropped the `Stalled` output (parked = STALLED caption + warning only, `_stalled` bool), renamed `Pass` → `Success Signal`/`SS`, and **reordered inputs: Stall Limit is now input 0, Signal input 1**.

All shifts break saved rigs' wiring on those params (GH reconnects by index). Builds compile clean (only .gha lock errors with Rhino open); live Rhino test pending. Related: [[signal-carrier-discipline]].
