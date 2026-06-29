---
name: python-output-list-access
description: RESOLVED 2026-06-29 — LLM Python script components output unreadable wrapped lists; real fix = turn MarshOutputs on (+ No Type Hint + List access). RhinoCode internals reference.
metadata: 
  node_type: memory
  type: project
  originSessionId: 84746927-a705-4772-8c7a-46abde294727
---

**RESOLVED 2026-06-29 (confirmed in live Rhino).** LLM-generated GH Python 3 Script components emitted an output assigned a Python list (`curves = []; curves.append(...)`); it landed as a single opaque `GH_ObjectWrapper<PyObject>` on the canvas (unreadable downstream → "Data conversion failed from Goo to Curve"; with a concrete type hint, a hard `ParamConvertException: PyObject → GeometryBase`).

**Root cause (the one that actually mattered): output marshalling was OFF.** RhinoCode's GH output assignment (`RhinoCodePlatform.Rhino3D.dll`, `Grasshopper1ScriptHelpers`/RunScript) flattens an output via `SetDataList` ONLY when the captured value is a **.NET enumerable** (`ConverterUtils.GetExpandKind(value)==Enumerable`). The captured value is `MarshOutput(obj)` only when the Outputs `ConvertPolicy` has the Marsh flag, i.e. `ConvertPolicy = MarshOutputs ? ConvertAndMarsh : Convert`. With `MarshOutputs` off the **raw Python object** is stored and GH wraps it as one `GH_ObjectWrapper<PyObject>` — regardless of access or type hint. The component defaults `m_marshOutputs=true`, but `SetScript(Grasshopper1Script)` copies the flag **from the script** (`m_owner.m_marshOutputs = script.MarshOutputs`), so a pushed script lands with it OFF. (`CPythonListMarsh.Cast` returns false for Outgoing, so list-access/converter do NOT drive output flattening — only `MarshOutputs` does.)

**The fix (three stacked changes, all in place; MarshOutputs is the substantive one):**
1. **`GhPythonBridge.EnableOutputMarshalling(obj)`** — sets public `MarshOutputs=true` (reflection). Called in `PyTransmitter.PushSolve` after output setup, before Expire. **This is what flattens the list.**
2. **`GhPythonBridge.SetOutputsNoTypeHint(obj, names)`** — sets each output's `IScriptParameter.Converter = null` (coerces to the Goo / No-Type-Hint converter, GUID `6A184B65`). Re-pins No Type Hint AFTER the engine's `VariableParameterMaintenance` swaps a Python Goo converter for the user's Default Python Hint (often "ghdoc Object", `1c282eeb`). The `Converter` setter routes through `ParamsApply` only (no re-swap), so it sticks. Without this, ghdoc wraps even marshalled values.
3. **Access promotion + in-place re-apply** — `PromoteListOutputs` (via `PythonOutputAccessInference`) marks list-valued outputs List access; `ApplyOutputAccess` re-applies in place via `ParamsApply` (the guard there fires on any matched param, NOT only on a value change — the first-push `AutoDeclare` clobber lives in the runtime ScriptParam, not in `param.Access`, so there is no value change to detect). Kept for the GH param display / per-element conversion.

**Dead ends (do not retry):**
- Forcing `Param.AutoDeclare=false` on the compiled signature → **EMPTY output.** The PythonNet capture loop reads the script-scope variable ONLY when `output.AutoDeclare` is true (`Rhino.Runtime.Code.Languages.PythonNet.dll`). AutoDeclare drives variable *capture*, not just access — it MUST stay true. (`ClearOutputAutoDeclare` method left in the bridge but uncalled.)
- Concrete type hint on a list output under Item access → fatal `PyObject→GeometryBase`. Outputs stay untyped (No Type Hint).

**Diagnostic that cracked it (kept):** `PyTransmitter` has a **Remark text output** (index 2) showing, per output, live `access`/`conv`/volatile-data shape (`GhPythonBridge.GetOutputDiagnostics` + `DescribeGoo`) AND the compiled signature `access`/`autoDeclare` (`GetCompiledOutputSignature`, reflection `Context→Script→GetCode()→Outputs`). Reading `GH_ObjectWrapper<PyObject>` vs flattened per-element goos was the ground truth at each step. Set in `ReadSolve` (`da.SetData`) and re-set every solve in `OnSolveTick`.

**Decompile workflow (for future RhinoCode spelunking):** `~/.dotnet/tools/ilspycmd.exe "<dll>" -o <dir>`. Key assemblies: `RhinoCodePluginGH.gha` (BaseScriptComponent, ScriptVariableParam, ParamsApply/UpdateCode, GetContextOutputs AutoDeclare); `RhinoCodePlatform.GH1.dll` (converter GUIDs — GH1GooConverter==GH1NullHint==No Type Hint `6A184B65`, GH1PythonDynamicConverter==ghdoc `1c282eeb`); `RhinoCodePlatform.Rhino3D.dll` (Grasshopper1Script, MarshOutputs→ConvertPolicy, output SetDataList/GetExpandKind); `Rhino.Runtime.Code.Languages.PythonNet.dll` (capture loop + MarshOutput + CPythonListMarsh).

Key files: `src/Physalia.GH/Generation/GhPythonBridge.cs`, `src/Physalia.GH/Components/GhPython/PyTransmitter.cs`, `src/Physalia.Core/Python/PythonOutputAccessInference.cs`. See [[system-prompt-preambles]], [[tools-in-use-component]].
