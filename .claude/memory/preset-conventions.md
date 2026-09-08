---
name: preset-conventions
description: "The canonical checklist for building a Physalia preset correctly — naming, where the files actually live, the tools-capable model rule, Picker discipline, wiring, and the STORED-vs-LOADED verification. Read before building one."
metadata:
  node_type: memory
  type: reference
---

Distilled 2026-09-07 from building three presets in one session. This is the checklist; the mechanics
of scripting a GH document live in [[building-harnesses-programmatically]], and the three presets
themselves are the worked examples ([[blender-mcp-preset]], [[comfy-render-preset]],
[[harness-builder-preset]]).

## Naming and placement

- **`<Model> - <Target>.gh`** — `Codex - Blender.gh`, `Codex - Rhino to ComfyUI.gh`,
  `Claude Code - Python 3.gh`. All eight pre-existing presets are plain `.gh`; `.phy` is the newer
  format and is read too, but matching the folder is the convention.
- **`Files/PRESETS/User/`, not `Physalia/`**, for anything that depends on machine-specific setup — an
  MCP server, a running external service, a CLI. `Physalia/` means "ships with the plug-in", and a
  shipped preset that cannot work out of the box is a broken default. `Watch and Repeat.gh` is the
  precedent for a repo-committed User preset.
- A preset **MUST contain a Chat** or the loader refuses it.

## Where preset files actually live — and the trap

`PresetLibrary.RootDir` is `<assembly dir>/Files/PRESETS`, i.e. **`bin/Debug/net7.0-windows/Files`,
not the repo.** So:

- The **repo** `Files/**` is the source of truth; `CopyLibraryFiles` syncs it into `bin` on every build.
- Writing only to the repo means the running Rhino **does not list your preset**. Writing only to
  `bin` means it is **not version-controlled** and the next `RemoveDir` in `CopyLibraryFiles` wipes it.
- So while iterating: write the repo copy, then **`cp` into `bin`** — preset, preamble, and any
  `PROJECT_FILES` payload. Data-only changes need no rebuild.
- Anything a harness GENERATES at runtime lands beside the plug-in and is therefore **not in git**.
  Say so when handing such a file to the user.

## The model node

- **If the pipeline has a Router, the model must be tools-capable.** Codex Model and the HTTP models
  (Anthropic/OpenAI/Gemini/OpenAI-compatible) are. **Claude Code Model is NOT** — its provider
  ignores the `tools` argument, so it never learns the tools exist and never calls one, while the
  pipeline solves perfectly green. This is the single most expensive mistake available here; see
  [[blender-mcp-preset]].
- **Codex Model is the current default** for these presets: local CLI, no API key, tools-capable.
  Store `gpt-5.6-luna` on its `Model` picker — read out of the shipped `Codex - Python 3.gh` rather
  than invented — and leave `Effort` UNWIRED (no picker is auto-placed there; empty means the
  model's own default).
- Codex needs the `codex` CLI installed and signed in, not a key. On this machine it is **not
  installed**, so every Codex preset shows exactly one warning saying so.

## Picker discipline — the easiest thing to get wrong

System Prompt and the model nodes auto-place a Picker on any input with **no source**, and they do it
**every time the file is LOADED** (deserialization adds objects before restoring wires). An input
left unwired in the file therefore grows a fresh Picker on every load, and the one on `Schema` snaps
to `values[0]` and folds a foreign JSON schema into the prompt.

- Every such input gets a **real, stored source**.
- Where a choice is wanted: keep the auto-placed Picker, set its value via the internal
  `SetSelectedValue` by reflection (no solve needed — `Write` serializes the field).
- Where nothing is wanted (`Schema`, for any prose pipeline): **drop the auto-picker and store a
  blank Panel** with a single space. Cut the wire first or `RemoveObject` will not stick.
  `SystemPrompt.Assemble` skips the schema paragraph on whitespace. A Picker cannot express "none".
- **Deleting pickers is not a fix and looks like one** — `ObjectCount` drops, the archive writes the
  smaller count, and they are re-made on load.

## Wiring

Forward wires are ordinary. **Every backward path is wireless** — a normal return wire gets
`Recursive data stream found` on the Conversation Log — and each hop needs its OWN
Feedback → Feedback Collector pair:

    LLM Call.Success Signal -> Conversation Log.Response Signal
    Router.Feedback         -> Conversation Log.LLM Tool Signal
    <each tool>.Result      -> Router.Results          (list input: several pairs may land here)

**One Router output per tool.** A fresh Router has `T1` + trailing `Feedback`; insert further outputs
at `Count - 1`. They rename themselves after the tools they reach on the first solve, which is the
cheapest proof dispatch is wired — never rename them by hand.

## What a preset CANNOT carry

Wiring and prompts, not per-machine configuration. MCP server commands/credentials stay in
`%LOCALAPPDATA%/Physalia/mcp-servers.json`; API keys stay in the encrypted store; provider
activations and API endpoints likewise. An `McpServer` node stores only the picked NAME. So a preset
is usually **half** the deliverable — say what else the user must add and where.

A preamble, by contrast, DOES travel: put it in `Files/SYSTEM_PROMPTS/PREAMBLE/<name>.txt` and point
the Preamble picker at that filename (with the `.txt`). Match the register of the shipped ones —
second person, direct, concrete about failure modes rather than encouraging.

## Verify before claiming it works

    stored = archive.GetRootNode.FindChunk("Definition").FindChunk("DefinitionObjects").GetInt32("ObjectCount")

**`stored` must equal what a load produces.** More on load means an input lacked a stored source and
grew a Picker. Also check: a Chat is present, picker count is what you intended, and no input has
`SourceCount == 0` with `PersistentData.DataCount > 1` (see the `SetPersistentData` traps in
[[building-harnesses-programmatically]]). Then place it through the real loader
(`HarnessComponent.ReadDocumentFile` → `CreateWith`) and solve: the Router output names and the MCP
node's Status line are what tell you the pipeline is actually live.
