---
name: interface-lock-grounder
description: "Interface Lock grounder (2026-07-31) — grip-links to a PyTransmitter, grounds + freezes the target script component's inputs/outputs; transmitter pushes code only under lock"
metadata: 
  node_type: memory
  type: project
  originSessionId: cf831060-3a67-43bd-8678-20534cb08dc2
  modified: 2026-08-01T05:16:37.459Z
---

# Interface Lock grounder (built 2026-07-31, not yet run live in Rhino)

New Grounding-section component that freezes a Python script component's I/O so LLM pushes can't break existing wires.

**Shape:** `InterfaceLock : PhyBase` (`Components/Grounding/InterfaceLock.cs`, GUID `B7D2F4A9-…0A46`) — no inputs, one `Param_Grounding` output, grip-links to a **PyTransmitter** (not the script component) via `InterfaceLockAttrib : GripLinkAttrib` with new `ArrowStyles.InterfaceLock` gradient (LimeGreen→Teal), anchor top-centre−6 of the transmitter. Link persisted as `LinkedGuid`. SolutionEnd signature watch (ToolsInUse pattern) re-solves when the target's interface changes.

**Core:** `ScriptInterfaceGrounding(ComponentName, Inputs, Outputs)` + `ScriptInterfacePort(Name, TypeHint, Access-string)` in `Grounding.cs`; renders the locked contract as verbatim-copyable `"inputs": [...]/"outputs": [...]` JSON entries (type omitted when untyped — schema allows that). Stable (not volatile). Falls through the ConversationLog `default` arm — **no chat-UI pill/panel was added** (no per-kind selection UI; add later if wanted).

**Enforcement (PyTransmitter):** `FindInterfaceLock()` scans the doc for an `InterfaceLock` where `Constrains(InstanceGuid)` (linked + not GH-disabled — disabling the lock suspends enforcement without unlinking). Under lock, `PushSolve` does `SetScript` + `Expire` ONLY — skips SetInputs/SetOutputs/SetOutputsNoTypeHint/EnableOutputMarshalling and leaves `_inputs/_outputs` empty so the access re-apply pass never runs. Validation: declared input/output names must be a **subset** of the target's live names (fewer is fine — params are never rebuilt; unknown = add/rename → reject). Violation → `_lockFeedback` routed as Fail (Warning level) containing the grounding's own contract rendering (reuse via `InterfaceLock.ToPorts` + `ScriptInterfaceGrounding.ToSystemPromptSection`), code NOT pushed.

**Type-hint read-back** (new, was impossible in repo before): `GhPythonBridge.GetInputSpecs/GetOutputSpecs` return `GhParamSpec`s; hint recovered via `IScriptParameter.Converter?.TargetType?.Type` (compile-time accessible — `IParamValueConverter.TargetType` is a `Rhino.Runtime.Code.ParamType` with `.Type`) reverse-mapped through `TypeHintMap`; null converter / unmappable / throw → empty (untyped). Outputs always empty hint by design.

**Why:** [[dead-wire-lint-projected-graph]] and friends guard the graph the LLM produces; this guards a HUMAN-owned component the LLM merely scripts. Related: [[human-tools-taxonomy-moves-2026-07]] (grounding taxonomy), [[python-output-list-access]] (why the push path mutates access at all).

Core tests: `ScriptInterfaceGroundingTests` (7 pass). Build clean. Live Rhino test pending — especially the Converter read-back (first time reading, not setting, the hint) and the lock-feedback loop end-to-end.
