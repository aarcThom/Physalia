---
name: system-prompt-preambles
description: Composer system-prompt assembly (PREAMBLE + SCHEMA folders) and the two preamble files
metadata: 
  node_type: memory
  type: project
  originSessionId: cf7dd6e5-8085-42a5-a51e-70a684ca7cc0
---

Composer (`Components/Core/Composer.cs`) assembles a system prompt from a **preamble** + a **schema**, each resolved from `Files/SYSTEM_PROMPTS/{PREAMBLE,SCHEMA}/` (canonical repo-root `Files/`, build-copied). Assembly = `{preamble}` + `"Your response must be valid JSON that conforms exactly to the following schema:"` + `{schema}`. The Picker dropdowns list files in those folders.

**Gotcha:** `Composer.IsTextFile` resolves only `.txt`/`.json`/`.yaml`/`.yml` — **`.md` is NOT picked up**. Preambles must be `.txt` (prose); schemas are `.json`.

Two preambles created 2026-06-13 in `Files/SYSTEM_PROMPTS/PREAMBLE/`:
- **`Python3 Script.txt`** — pairs with `SCHEMA/PythonComponent.json` (writes a GH Python 3 Script component: inputs/outputs as named vars, RhinoCommon, tolerate unconnected inputs).
- **`Node Graph.txt`** — format-agnostic, pairs with either `SCHEMA/GhJSONSchema.json` (direct path → [[component-transmitter]]) or `SCHEMA/PhySchema.json` (layout-pass path → SchemaTranslator). Frames decomposing a request into wired Grasshopper components; defers exact field/connection/value-encoding rules to the appended schema.

Both end with "emit nothing but the final JSON object" and mention the error-feedback loop (a failed/disconnected component returned as feedback → fix and resubmit), matching the Reasoner→Auditor→PyTransmitter/ComponentTransmitter agentic loop. Build copies them to `bin\...\Files\SYSTEM_PROMPTS\PREAMBLE\` via the same `CopyLibraryFiles` glob.
