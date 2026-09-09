---
name: system-prompt-preambles
description: System Prompt assembly (PREAMBLE + SCHEMA folders + the Additional Prompt input) and the preamble/schema file pairs
metadata: 
  node_type: memory
  type: project
  originSessionId: cf7dd6e5-8085-42a5-a51e-70a684ca7cc0
  modified: 2026-08-17T00:00:00.000Z
---

System Prompt (`Components/Pipeline/SystemPrompt.cs`) assembles a system prompt from a **preamble** + a **schema**, each resolved from `Files/SYSTEM_PROMPTS/{PREAMBLE,SCHEMA}/` (canonical repo-root `Files/`, build-copied). Assembly = `{preamble}` + `"Your response must be valid JSON that conforms exactly to the following schema:"` + `{schema}`. The Picker dropdowns list files in those folders.

**2026-09-08: a SIBLING component — `Project Prompts`** (`Components/Pipeline/ProjectPrompts.cs`, Pipeline tab). The same assembly one folder over: a `Prompt File` resolved from **this harness's project folder** (`Files/PROJECT_FILES/<harness>`) plus an `Additional Prompt` appended verbatim, out as one `System Prompt` text. No schema, so no schema output and no schema sentence.

**Why it is a separate component rather than a spelling of the existing input.** `Files/SYSTEM_PROMPTS` ships with the plug-in and is shared by every pipeline on the machine — so a brief written for one job either does not belong in there or turns up in every other pipeline's Picker. A project folder is the work's own material and travels inside a `.phy`, which is exactly what a site description or an office standard wants. Same argument as MemoryTool's `Memory Folder` and ReadPdf's `PDF Folder`: what a pipeline knows should ship with the pipeline.

Four decisions worth not re-litigating:
- **No `Project Folder` input.** The harness's own folder IS the premise; `ProjectFolderInput.Resolve(this, null)` gets it. Outside a harness that resolves to the `unnamed` fallback, so the node emits a **Remark** rather than reading it silently. If a pipeline ever needs to follow a folder override typed on the grounder, add the input LAST (index 2) — the same param-layout rule the Additional Prompt was subject to.
- **`.txt` and `.md` only** — narrower than `SystemPrompt.IsTextFile`'s `.txt/.json/.yaml/.yml`. A project folder holds `conversation.json`, `downloads.json` and `runs.jsonl`; offering the pipeline's own bookkeeping as a system prompt is noise. `.md` is IN here (unlike SYSTEM_PROMPTS, where its absence is a gotcha) because a hand-written brief is naturally markdown and nothing in this folder plays the schema role.
- **Top level only.** A project folder has `PDF/` and `conversation-images/` under it, which would bury the one or two files this node is about.
- **File resolution goes through `FileRead.TryResolve`**, so the containment rule is the one every other project-file node uses; a value landing outside the folder resolves to no file and is therefore sent AS TYPED, never read.

**Refresh is the `ProjectFolderGrounder` problem again** and the code is deliberately a copy of it: a file appearing in a folder is not on GH's data graph, so a debounced (750ms) `FileSystemWatcher` calls `ExpireSolution(false)` **only** — this sits upstream of the Conversation Log, so the solve the next prompt causes rescans, and a `ScheduleSolution` posted from a watcher thread is dropped by a disabled harness sub-document. The Picker refresh itself is `SystemPrompt.RefreshListIfChanged` copied verbatim, Picker-expiry included (a Picker solves BEFORE the node it feeds). Carries **Open Project Folder** on its menu, which is where you put a prompt file.

**Not run in Rhino yet, and it has no icon** — it is the first component to fall back to `brain.png` since the 2026-08-17 icon pass, when "nothing falls back any more" was true.

**2026-08-17: a THIRD input — `Additional Prompt` ("AP").** Optional plain text, appended VERBATIM as the last section of the assembled prompt (after the schema block) — for per-canvas instructions that don't deserve their own file in `PREAMBLE/`. Three deliberate non-features, each one a trap avoided: it does **not** go through `Resolve`, so text that happens to match a filename in `PREAMBLE`/`SCHEMA` is still sent as typed; it is **not** in `IPickableValuesSource.Inputs`, so `AddedToDocument` places no third Picker; and it is registered **LAST (index 2)**, because inserting it anywhere else would shift the param layout of every saved `.gh` and every preset (the same rule CLAUDE.md states for the base-appended Signal on a `RoutingComponentBase`). Blank/whitespace-only text is dropped like the other two sections.

**Where it lands in the cache:** the component's output is one flat string, which the Conversation Log folds in as a single **Stable** `SystemPromptSegment` — so additional text sits inside the cacheable prefix (see `Core/ConvoInstruct/SystemPrompt.cs`). Fine while it is authored once, but wiring anything that rewrites it per turn would invalidate the whole cached prefix.

**Gotcha:** `System Prompt.IsTextFile` resolves only `.txt`/`.json`/`.yaml`/`.yml` — **`.md` is NOT picked up**. Preambles must be `.txt` (prose); schemas are `.json`.

**2026-07-31: a fourth preamble with NO schema of its own** — `PREAMBLE/Python3 Script (Small Model).txt`, for small models (Qwen 14b class): mandates `search_rhinocommon` lookups before writing/fixing code, caps planning at ~3 sentences (anti-spiral), pushes commit-fast-and-let-interpreter-feedback-fix. Pairs with the existing `SCHEMA/Python3 Script.json` (Preamble and Schema are independent Picker inputs, so an unpaired preamble is fine).

**2026-08-11: a fourth symmetric pair** — **`PREAMBLE/C# Script.txt` ↔ `SCHEMA/C# Script.json`** (title `CSharpComponent`), for the Rhino 8 C# Script component driven by [[csharp-transmitter]]. Same `{code, inputs, outputs}` shape as the Python pair, but it also pins the `Script_Instance` / `RunScript` boilerplate and the hint→C# type table, because the signature is a second declaration of the interface that must agree with the JSON. Added to `PromptSchemaAssetTests`.

**THREE symmetric pairs as of 2026-07-27** (a third was added for staged generation — see [[incremental-staged-building]]):
- **`PREAMBLE/Incremental Node Graph.txt` ↔ `SCHEMA/Incremental Node Graph.json`** — one stage per response, plan block ahead of the JSON.

**Cleaned up 2026-07-07 to two symmetric pairs** (all minified variants, PhySchema, GhJSONSchema, and the standalone GhPatchSchema DELETED; the dead PhySchema code path — `Generation/PhySchema.cs`, `GhJsonBridge.SerializePhySchema`, the `HierarchicalLayout` PhySchema overload — deleted with them):
- **`PREAMBLE/Node Graph.txt` ↔ `SCHEMA/Node Graph.json`** — the node-based pair, INCLUDING patching: the schema is a `oneOf` umbrella (full GhJSON document | ghpatch), the preamble carries the mode rule (canvas state present → emit ghpatch; else full document), instanceGuid matching, checksum copy, and the physalia.rhinoRef rule. See [[iterative-canvas-editing]].
- **`PREAMBLE/Python3 Script.txt` ↔ `SCHEMA/Python3 Script.json`** (renamed from Python3Schema.json) — writes a GH Python 3 Script component.

Both end with "emit nothing but the final JSON object" and mention the error-feedback loop (a failed/disconnected component or unapplied patch op returned as feedback → fix and resubmit). `PromptSchemaAssetTests` self-validates each schema's embedded examples against itself (this caught a stray `description` field in the Python example). Build copies to `bin\...\Files\SYSTEM_PROMPTS\` via the `CopyLibraryFiles` glob.
