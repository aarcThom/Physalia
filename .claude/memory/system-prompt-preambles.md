---
name: system-prompt-preambles
description: System Prompt system-prompt assembly (PREAMBLE + SCHEMA folders) and the two preamble files
metadata: 
  node_type: memory
  type: project
  originSessionId: cf7dd6e5-8085-42a5-a51e-70a684ca7cc0
---

System Prompt (`Components/Core/SystemPrompt.cs`) assembles a system prompt from a **preamble** + a **schema**, each resolved from `Files/SYSTEM_PROMPTS/{PREAMBLE,SCHEMA}/` (canonical repo-root `Files/`, build-copied). Assembly = `{preamble}` + `"Your response must be valid JSON that conforms exactly to the following schema:"` + `{schema}`. The Picker dropdowns list files in those folders.

**Gotcha:** `System Prompt.IsTextFile` resolves only `.txt`/`.json`/`.yaml`/`.yml` — **`.md` is NOT picked up**. Preambles must be `.txt` (prose); schemas are `.json`.

**Cleaned up 2026-07-07 to exactly TWO symmetric pairs** (all minified variants, PhySchema, GhJSONSchema, and the standalone GhPatchSchema DELETED; the dead PhySchema code path — `Generation/PhySchema.cs`, `GhJsonBridge.SerializePhySchema`, the `HierarchicalLayout` PhySchema overload — deleted with them):
- **`PREAMBLE/Node Graph.txt` ↔ `SCHEMA/Node Graph.json`** — the node-based pair, INCLUDING patching: the schema is a `oneOf` umbrella (full GhJSON document | ghpatch), the preamble carries the mode rule (canvas state present → emit ghpatch; else full document), instanceGuid matching, checksum copy, and the physalia.rhinoRef rule. See [[iterative-canvas-editing]].
- **`PREAMBLE/Python3 Script.txt` ↔ `SCHEMA/Python3 Script.json`** (renamed from Python3Schema.json) — writes a GH Python 3 Script component.

Both end with "emit nothing but the final JSON object" and mention the error-feedback loop (a failed/disconnected component or unapplied patch op returned as feedback → fix and resubmit). `PromptSchemaAssetTests` self-validates each schema's embedded examples against itself (this caught a stray `description` field in the Python example). Build copies to `bin\...\Files\SYSTEM_PROMPTS\` via the `CopyLibraryFiles` glob.
