---
name: script-io-grounder
description: "Script I/O grounder (was Interface Lock) — grip-links to a script transmitter, grounds + freezes the target script component's inputs/outputs; transmitter pushes code only under lock"
metadata: 
  node_type: memory
  type: project
  originSessionId: cf831060-3a67-43bd-8678-20534cb08dc2
  modified: 2026-08-12T05:08:50.412Z
---

# Script I/O grounder (built 2026-07-31 as "Interface Lock", renamed 2026-08-11; not yet run live in Rhino)

Grounding-section component that freezes a script component's I/O so LLM pushes can't break existing wires. **The old name "Interface Lock" is gone** — display name AND nickname are now `Script I/O`, class/file `ScriptIO` (`Components/Grounding/ScriptIO.cs`), attrib `ScriptIOAttrib`, gradient `ArrowStyles.ScriptIO`, transmitter-side accessor `ScriptTransmitterBase.ActiveScriptIO`. **ComponentGuid pinned** (`B7D2F4A9-…0A46`), per the [[component-rename-plainspoken]] precedent, so saved `.gh` files round-trip. Behaviour words ("locked interface", `RespectsLockedInterface`, the grounding's "LOCKED interface" prose) were deliberately KEPT — locking is still what it does; only the component's name changed. Read the rest of this file with the old name mentally substituted.

**Shape:** `InterfaceLock : PhyBase` (`Components/Grounding/InterfaceLock.cs`, GUID `B7D2F4A9-…0A46`) — no inputs, one `Param_Grounding` output, grip-links to a **PyTransmitter** (not the script component) via `InterfaceLockAttrib : GripLinkAttrib` with new `ArrowStyles.InterfaceLock` gradient (LimeGreen→Teal), anchor top-centre−6 of the transmitter. Link persisted as `LinkedGuid`. SolutionEnd signature watch (ToolsInUse pattern) re-solves when the target's interface changes.

**Core:** `ScriptInterfaceGrounding(ComponentName, Inputs, Outputs)` + `ScriptInterfacePort(Name, TypeHint, Access-string)` in `Grounding.cs`; renders the locked contract as verbatim-copyable `"inputs": [...]/"outputs": [...]` JSON entries (type omitted when untyped — schema allows that). Stable (not volatile). Falls through the ConversationLog `default` arm — **no chat-UI pill/panel was added** (no per-kind selection UI; add later if wanted).

**Enforcement (PyTransmitter):** `FindInterfaceLock()` scans the doc for an `InterfaceLock` where `Constrains(InstanceGuid)` (linked + not GH-disabled — disabling the lock suspends enforcement without unlinking). Under lock, `PushSolve` does `SetScript` + `Expire` ONLY — skips SetInputs/SetOutputs/SetOutputsNoTypeHint/EnableOutputMarshalling and leaves `_inputs/_outputs` empty so the access re-apply pass never runs. Validation: declared input/output names must be a **subset** of the target's live names (fewer is fine — params are never rebuilt; unknown = add/rename → reject). Violation → `_lockFeedback` routed as Fail (Warning level) containing the grounding's own contract rendering (reuse via `InterfaceLock.ToPorts` + `ScriptInterfaceGrounding.ToSystemPromptSection`), code NOT pushed.

**Type-hint read-back** (new, was impossible in repo before): `GhPythonBridge.GetInputSpecs/GetOutputSpecs` return `GhParamSpec`s; hint recovered via `IScriptParameter.Converter?.TargetType?.Type` (compile-time accessible — `IParamValueConverter.TargetType` is a `Rhino.Runtime.Code.ParamType` with `.Type`) reverse-mapped through `TypeHintMap`; null converter / unmappable / throw → empty (untyped). Outputs always empty hint by design.

**Why:** [[dead-wire-lint-projected-graph]] and friends guard the graph the LLM produces; this guards a HUMAN-owned component the LLM merely scripts. Related: [[human-tools-taxonomy-moves-2026-07]] (grounding taxonomy), [[python-output-list-access]] (why the push path mutates access at all).

**SUPERSEDED IN PART, 2026-08-11 — the lock is no longer Python-only.** See [[csharp-transmitter]]. What changed: `IsValidTarget` / `TryResolveTargetScript` widened `PyTransmitter` → `ScriptTransmitterBase`; `FindInterfaceLock`/`ValidateAgainstLockedInterface`/`BuildLockFeedback` moved OFF PyTransmitter onto that base as `ActiveScriptIO`/`RespectsLockedInterface`; `ScriptInterfaceGrounding` gained a required 4th param, `ScriptInterfaceDialect` (Core — `ComponentKind`/`SchemaName`/`CodeRule`, `.Python` and `.CSharp`), read off `ScriptTransmitterBase.Dialect` so no language branch lives in the lock. The **subset** rule above is now Python-specific: `AllowsPartialInterface` is false for C#, where the RunScript signature restates the interface and an omitted param has nothing to bind to.

Core tests: `ScriptInterfaceGroundingTests` (7 pass; 9 after the dialect pair). Build clean. Live Rhino test pending — especially the Converter read-back (first time reading, not setting, the hint) and the lock-feedback loop end-to-end.
