---
name: python-output-list-access
description: Fix for LLM Python script components outputting unreadable lists — deterministic item→list output access promotion + preamble
metadata: 
  node_type: memory
  type: project
  originSessionId: 84746927-a705-4772-8c7a-46abde294727
---

**Problem:** LLM-generated GH Python 3 Script components consistently emitted an output assigned a Python list (e.g. `profiles = [curve, curve, ...]`) but declared the output as `item` access (or omitted `access`, which silently defaults to `item` in `PyTransmitter.ParseParams`). GH then wraps the whole list as one opaque object ("One locally defined value… [<Rhino.Geometry.NurbsCurve …>, …]"), unreadable by downstream geometry components.

Root cause was **model compliance**, not missing plumbing — the schema (`Python3Schema.json` `access: item|list|tree`) and bridge (`GhParamSpec`→`GhScriptParamAccess`→`ScriptParamAccess`) already carried access correctly end-to-end.

**Fix (2026-06-28, builds clean):**
1. **Deterministic guard (the real fix):** new pure Core helper `Physalia.Core/Python/PythonOutputAccessInference.cs` → `InferListVariables(code, names)`: conservative static heuristic (regex) detecting `name = [...]` / comprehension / `name = list(...)` / `+= [` / `.append/.extend/.insert`. Wired into `PyTransmitter.TryParse` via private `PromoteListOutputs(code, outputs)` — promotes any **output** declared `Item` but assigned a list to `List`. Outputs-only; never downgrades; `tree` untouched. So a model slip can't produce a broken component.
2. **Prompt reinforcement (cheap insurance):** added an emphatic CRITICAL rule (assign a list → access MUST be `list`; names the symptom) to both `Files/SYSTEM_PROMPTS/PREAMBLE/Python3 Script.txt` (+ Minified) and the `rules` in both `SCHEMA/Python3Schema.json` (+ Minified).

Heuristic can't follow dataflow through intermediate vars (false negative → falls back to declared access; harmless). See [[system-prompt-preambles]], [[tools-in-use-component]]. Files staged to bin via CopyLibraryFiles (Debug built; rebuild -c Release for Release output). Live Rhino test pending.
