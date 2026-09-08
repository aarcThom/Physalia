---
name: harness-builder-preset
description: "The Codex - Harness Builder meta-preset (a harness that writes harnesses), proof the whole build recipe runs in run_rhino_script's CPython, and the FOUR Physalia component names that are ambiguous by name alone."
metadata:
  node_type: memory
  type: project
---

Built 2026-09-07. `Files/PRESETS/User/Codex - Harness Builder.gh` — the user describes a pipeline,
this harness writes it as a preset and verifies it loads. 22 objects, `STORED=22 LOADED=22 pickers=2
hasChat=True`. Tools: **Drive Rhino** (how a preset actually gets constructed — Python inside Rhino
with the Grasshopper object model) and **Ask Human** (a harness spec has required fields and there is
no safe value to invent for any of them). Live: Router outputs renamed to
`['run_rhino_script', 'ask_human', 'Feedback']`.

**Its preamble IS the deliverable.** `Harness Builder.txt` (~10.5k chars) encodes the whole scripted-
build contract: name+section resolution, the forward wiring, the wireless backward hops, the Picker
discipline, the Router variable-output recipe, `SetPersistentData` clearing and wrapping, runtime
discovery of the Files folder, and the STORED-vs-LOADED verification with an instruction to report
both numbers. It is [[building-harnesses-programmatically]] plus [[blender-mcp-preset]] plus
[[comfy-render-preset]], written as instructions to a model instead of notes to a human.

## The recipe runs in run_rhino_script's CPython — verified before writing the preamble

My own build scripts all ran under `-RunPythonScript` (**IronPython 2.7**); the tool runs Rhino's
embedded **CPython 3.9.10 + pythonnet**. Different engine, so the recipe was tested there first
rather than assumed — the mistake I made once already with the `System.Drawing` snippet
([[comfy-render-preset]]). All of it works unchanged: `clr.AddReference("Grasshopper")`,
`ComponentServer.EmitObject`, the Router variable output, the **internal** `SetSelectedValue` reached
by reflection, `System.Array[System.Object]([...])`, `AddCollector`, `GH_Archive.WriteToFile`, and a
re-read reporting `STORED=10 LOADED=10 stable=True`.

**Proved end to end**: fired through the preset's own Drive Rhino, the recipe generated
`Codex - PDF Questions.gh` (18 objects, its own preamble written too), which then appeared in
`PresetLibrary.Enumerate` and loaded through `HarnessComponent.ReadDocumentFile` at 18 objects with
2 pickers and a Chat — i.e. the generated preset is stable by the same measure as a hand-built one.
The demo was deleted afterwards; it is reproducible.

## FOUR component names are ambiguous, and resolving by name alone silently picks the wrong one

Resolving by name is better than a frozen guid table (it cannot go stale) — but it is not enough.
Filtering `Desc.Category == "Physalia"` and matching `Desc.Name` still leaves four collisions:

| Name | claimed by |
|---|---|
| `Read PDF` | `Human Tools` (**AddPdf**, NO inputs) and `LLM Tools` (**ReadPdf**, has `Signal`) |
| `Component Catalog` | `Params` (hidden proxy) and `Grounding` |
| `Model API` | `Params` (hidden proxy) and `Models` |
| `Token Estimator` | `Params` (hidden proxy) and `Tokens & Compaction` |

Three are against hidden `Param_*` proxies (`PhyParam` sets `GH_Exposure.hidden`, so they never reach
the ribbon but they ARE in `ObjectProxies`). **`Read PDF` is the dangerous one** — two real components,
and the human-tool half has no parameters at all, so the build dies on
`no input Signal on Read PDF`. Caught exactly this way while generating a PDF pipeline. So: match on
`Desc.SubCategory` too (it is the ribbon section), and **check the emitted object has the inputs you
are about to wire** rather than trusting the lookup.

## Driving a tool that marshals to Idle, from a script

Repeating the trap from [[comfy-render-preset]] because it bit again here: `run_rhino_script` runs on
`RhinoApp.Idle`, so a driver script must **fire and exit** — never sleep or wait. And since a manual
Construct Tool Call batch discards the result, wrap the payload so it reports itself:

```python
open(r"…\tool-ran.txt","w").write("ran")          # proves the tool executed at all
try:    exec(open(r"…\gen.py", encoding="utf-8").read())
except Exception:
    import traceback; open(r"…\exec-err.txt","w").write(traceback.format_exc())
```

That three-way split — tool-ran / exec-err / the generator's own log — is what distinguishes "the
signal never arrived", "the script failed to compile" and "the build failed", which otherwise all
look identical (nothing happens, no error anywhere). **A SyntaxError in an `exec`'d file cannot be
caught by a `try` inside that same file**, so it produces no log at all; syntax-check generator files
before firing them.
