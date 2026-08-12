---
name: csharp-transmitter
description: "The C# Transmitter (grip \"C#\"), its signature-agreement guard, and the language test every script transmitter now needs"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5eabf66c-d38d-414b-8bbf-e50f65864dd1
  modified: 2026-08-12T05:08:23.264Z
---

**2026-08-11: added `CsTransmitter`** (`src/Physalia.GH/Components/Transmitters/CsTransmitter.cs`) — a `ScriptTransmitterBase` sibling of PyTransmitter that pushes LLM-generated C# into a linked **Rhino 8 C# Script** component. Harness outlet label is `"C#"`; gradient `ArrowStyles.CsTransmitter` (BlueViolet → Turquoise). System prompt pair: `Files/SYSTEM_PROMPTS/PREAMBLE/C# Script.txt` + `SCHEMA/C# Script.json` (title `CSharpComponent`).

**The thing that makes C# different from Python, and the reason for the guard:** a Rhino 8 C# script declares its parameters TWICE — once in the submission JSON, once in the `RunScript` signature the engine reads out of the source. Verified by reflection over the shipped assemblies: the engine's own patterns are
```
public\s+class\s+Script_Instance\s+:\s+GH_ScriptInstance
private\s+(async\s+)?void\s+RunScript\((?<params>[^)]*)\)
(ref|out)\s                                   ← by-ref params are the OUTPUTS
(IEnumerable|IList|List|DataTree)\<(?<type>.+)\>   ← how it reads list/tree access back
```
(strings live in `RhinoCodePlatform.Rhino3D.dll`, UTF-16). `CsTransmitter.TryCheckSignature` copies those two regexes **verbatim, whitespace classes and all** — a guard looser than the thing it guards passes source that then dies on the canvas. It rejects a disagreeing submission BEFORE the push and puts the expected signature in the Fail feedback, so the model fixes it by copying rather than guessing.

**Language predicates are now mandatory.** Every Rhino 8 script component (Python 3, IronPython 2, C#) implements the same `IScriptComponent`; only `LanguageSpec` separates them. `GhPythonBridge` gained `IsPython3Component` / `IsCSharpComponent` over a private `SpeaksLanguage`. Note `Matches` reads **scope-first** — the wildcard-holding spec is the RECEIVER: `LanguageSpec.CSharp` (`*.*.csharp@*.*`) `.Matches(actual)`. PyTransmitter's `IsLinkTarget` was narrowed from "any script component" to Python 3 at the same time; before this it would happily have linked to a C# node.

Other decisions:
- **No marshalling repairs on the C# side.** `EnableOutputMarshalling`, `SetOutputsNoTypeHint`, the access re-apply / `ClearOutputAutoDeclare` pass are Python-engine fixes (see [[python-output-list-access]]). C# hands GH typed values already. Outputs are still pushed as `ParamType.Any`; the model writes `out object x` / `out List<object> x`.
- **No unconnected-input filter.** Python's filter keys off `name 'x' is not defined`; C# just receives null/default. Instead the Fail feedback NAMES the unconnected inputs so the model isn't sent chasing a bug that is really a missing wire.
- **No icon** — falls back to `brain.png`, like TextTransmitter/Harness/ScriptIO.
- DRY: the shared `{code, inputs, outputs}` parse moved out of PyTransmitter into `Generation/ScriptComponentJson.cs`; PyTransmitter now calls `PromoteListOutputs` itself in `PushSolve`.
- `PromptSchemaAssetTests.SchemaAsset_OwnExamples_Validate` gained a `C# Script.json` row — 437 Core tests pass.

**Script I/O now covers both languages** (same session; the component was called Interface Lock until it was renamed later the same day — see [[script-io-grounder]]). `ScriptIOAttrib.IsValidTarget` and `ScriptIO.TryResolveTargetScript` widened from `PyTransmitter` to `ScriptTransmitterBase`; enforcement (`ActiveScriptIO`, `RespectsLockedInterface`, the rejection feedback) moved OFF PyTransmitter and ONTO that base, so a new script transmitter is lockable for free.

The design point: **a parameter set is language-neutral, the prose about it is not.** So `ScriptInterfaceDialect` (Core, beside `ScriptInterfaceGrounding`) carries the three strings that vary — `ComponentKind`, `SchemaName`, `CodeRule` — with `.Python` / `.CSharp` statics, and `ScriptInterfaceGrounding` gained it as a required 4th positional param. `ScriptTransmitterBase.Dialect` is abstract and public precisely so the lock can read it off whatever it is linked to. No language branch anywhere in `ScriptIO`.

One real behavioural difference: `AllowsPartialInterface` (virtual, true on the base). Python may declare a SUBSET of the locked params — unmentioned variables are simply unused. C# may NOT: the RunScript signature is the component's second declaration of its interface, and a param the signature omits has nothing to bind to. CsTransmitter overrides it false, and the lock check then reports "locked inputs you left out" as well as unknown names. When locked, the two C# checks compose: lock pins declared==target, signature pins code==declared.

**NOT yet run in Rhino.** The open live question is whether pushing params via `UpdateInput/OutputParameters` and letting the engine auto-declare from the signature agree in practice, or whether the auto-declare pass makes the explicit push redundant. Related: [[harness-subdocument]], [[system-prompt-preambles]], [[script-io-grounder]].
