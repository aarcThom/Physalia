---
name: blender-mcp-preset
description: "The Codex - Blender preset (drives Blender over MCP), and the three traps behind it: the Claude Code provider IGNORES tools so it can never drive a Router, Rhino's PYTHONHOME kills any uvx/Python MCP server, and an unwired input grows a fresh Picker every time a preset is LOADED."
metadata:
  node_type: memory
  type: project
---

Built 2026-09-07. `Files/PRESETS/User/Codex - Blender.gh` — a harness that lets a person type
modelling commands in the chat and have them run in a live **Blender**, through the `blender` MCP
server (`uvx blender-mcp`, 28 tools). 19 objects, verified live: connected, **28 tools advertised**,
Router output renamed to `Blender`, and a real cube created in Blender through the pipeline (driven
with Construct Tool Call, so no model quota was spent proving dispatch).

## THE MODEL MUST BE TOOLS-CAPABLE, AND CLAUDE CODE IS NOT

The first cut wired a **Claude Code Model** and was wrong in a way nothing on the canvas reports.
`ClaudeCodeProvider.StreamAsync` **ignores its `tools` argument** — its own remark says "the CLI
invocation does not advertise Physalia tool definitions to the model" — so the model is never told
the Blender tools exist, never emits a tool call, and the Router never fires. The pipeline solves
**perfectly green**: 28 tools advertised, Router output correctly renamed, zero runtime messages. The
only symptom would be a model that chats about Blender and never touches it, which reads as a weak
prompt rather than a structural dead end. **Check the provider before wiring a Router into anything.**
Tool-capable: the HTTP providers, and **Codex** (via `dynamicTools`). Not: Claude Code.

The preset settled on a **Codex Model** (2026-09-07, at the user's direction). Note what Codex is:
a LOCAL-CLI provider deriving from `PhyBase`, so it has **no Model API input and takes no API key** —
it drives the `codex` CLI the user is signed into, exactly as Claude Code does, and differs from it
only in advertising Physalia's tools. Swapping to it therefore DELETES the Model API node rather
than re-pointing it. The Anthropic-over-HTTP version built first is equally valid and is the fallback
if a subscription CLI is unavailable.

None of this bit the shipped Claude Code presets, because those are code-GENERATION pipelines
(Detect JSON → Schema Validator → transmitter) that never dispatch a tool.

**The pipeline is the canonical tool loop and nothing more** — Chat → Conversation Log → LLM Call →
Router → MCP Server, with a Codex Model into the LLM Call, Tools Present into Grounding, Add Image
on Human Tools, a Panel on the LLM Call's Fail Signal, and the three wireless backward hops from
[[building-harnesses-programmatically]].
It carries **no transmitter**: nothing here writes to the Grasshopper canvas, which is also what the
preamble tells the model in as many words.

**A preset CANNOT carry the MCP server.** `McpServer` stores only the picked NAME (and which tools
are advertised); the command, args and `env` stay in `%LOCALAPPDATA%/Physalia/mcp-servers.json`,
per-machine, because a token serialized onto a component would ship inside every preset made from
it. So the preset is half the deliverable — the other half is that file, and a machine without it
gets the legible `'blender' is not one of your configured MCP servers.`

**RHINO'S `PYTHONHOME` KILLS EVERY PYTHON MCP SERVER, and the error names nothing to do with it.**
Rhino 8 sets `PYTHONHOME=\\?\C:\Users\<u>\.rhinocode\py39-rh8` for its own embedded CPython 3.9. A
child `uvx` process inherits it, its newer Python tries to load Rhino's 3.9 stdlib, and the server
dies at startup with:

```
Could not import runpy module
ModuleNotFoundError: No module named 'importlib._abc'
```

Physalia reports it faithfully as "closed the connection", which reads as a broken server rather
than a poisoned environment — and the SAME `uvx blender-mcp` runs perfectly from a shell, which is
what makes it look like Physalia's fault. The fix is per-entry, in `mcp-servers.json`:
`"env": { "PYTHONHOME": "", "PYTHONPATH": "" }`. It works because **CPython's `_Py_GetEnv` treats an
empty value as unset**, and because `McpSession` applies env as `startInfo.Environment[key] = value`.
`env` is part of `McpServerDefinition.Identity`, so editing it correctly drops the warm session.
**Expect this for any stdio MCP server that is a Python program** — it is not Blender-specific.

**Blender itself was running in WSL, and the server still belongs on WINDOWS.** Blender listens on
WSL's own `127.0.0.1:9876`; WSL2 localhost forwarding makes that reachable from Windows (verified),
so plain `uvx blender-mcp` on the Windows side drives it. **Do not route stdio through `wsl.exe`** —
`wsl.exe -e /bin/cat` lands in an interactive `bash` instead of running `cat`, and PowerShell's
`WriteLine` leaks a `\r` into Linux. Both were measured; the Windows-side server has neither problem.

## The Picker trap — an unwired input grows a Picker on every LOAD, not just on placement

This is the one that cost the most, and it is a hazard for **any** scripted preset.

`SystemPrompt.AddedToDocument` (and the model nodes) place a Picker **and wire it** for any input
whose `SourceCount == 0`. Deserialization adds each object BEFORE restoring wires, so an input left
unwired in the FILE is unwired at that moment — and grows a **fresh Picker every single time the
preset is read**. Measured: a file storing 16 objects and no Pickers loads as 19 objects with 3.

Why it matters rather than merely being untidy: the auto-Picker on **Schema** snaps to `values[0]`
and folds another pipeline's JSON schema into the prompt. On a Blender preset that silently orders
the model to answer in the C# submission format.

- **Removing the Picker is not the fix, and appears to work.** `doc.RemoveObject` does not stick
  while the picker is still a source — `ObjectCount` drops, the archive writes the reduced count, and
  the pickers are simply re-made on read. Cutting the wire first (`RemoveAllSources`) removes them
  from the FILE and still does not help, because the re-placement is triggered by the load.
- **The fix is a real, STORED source on every such input**, which is what the shipped presets have
  (their Pickers are in the file). Then `SourceCount != 0` at load and nothing is placed. Verified:
  `STORED=19 LOADED=19 pickers_after_load=2`.
- **Where a choice is wanted, store a Picker** and set its value with the internal
  `SetSelectedValue` by reflection — no solve needed, `Write` serializes the field, and the value is
  honoured on load once the owner populates the list.
- **Where NOTHING is wanted, store a blank Panel** (a single space). `SystemPrompt.Assemble` skips
  the schema paragraph on whitespace, so the prompt comes out clean. A Picker cannot express "none":
  an empty pick snaps to `values[0]`.

## Driving Rhino from WSL to author a preset

- **`RhinoCode.exe -r <id> script <file>` reports success and executes NOTHING** against a running
  instance. `list` sees the instance; the script never runs. Do not trust its exit code.
- What works is `-RunPythonScript "<path>"` typed into Rhino's command line by `SendKeys`, but the
  **Grasshopper window steals the keystrokes** — minimize it and raise the Rhino main window first.
  This is why the channel appeared to work once and then silently stop.
- **Read a preset with `GH_Archive` + `ExtractObject`, never `GH_DocumentIO.Open`.** The Open path
  goes through the document server and pops modal dialogs — a missing-plug-in prompt and a
  **Grasshopper Font Mapper** that re-fires per object and ignores `{ENTER}` (click its button). The
  archive route raised none of them. Confirms the note in
  [[building-harnesses-programmatically]].
- **The stored `ObjectCount` in the archive chunk tree is the only honest count**:
  `root.FindChunk("Definition").FindChunk("DefinitionObjects").GetInt32("ObjectCount")`. Materializing
  objects runs the side effects you are trying to measure.
- `DocumentServer.DocumentCount` (not `.Count`); `canvas.set_Document` does **not** exist under
  `-RunPythonScript`'s IronPython — reach the property setter by reflection.

**The shipped presets still contain a `Harness Notes` object**, which CLAUDE.md says none of them
ever carried. Harmless on the real path (`ReadDocumentFile` uses `GH_Archive`, which drops it
silently — 75 stored, 74 loaded), but `GH_DocumentIO.Open` prompts to download a missing plug-in.

**With no `codex` CLI installed the preset reports exactly one warning**, and it is the whole setup
instruction: *"Codex CLI not found on PATH. Install it (npm install -g @openai/codex) and run
`codex login`."* Everything else solves clean, MCP included.

The Anthropic version reported TWO instead, and the second is worth knowing about: `Anthropic Model`
says *"Wire a Model API component to say which provider to use"* even when the wire IS there
(verified) — it says that whenever the input carries no DATA, which is what an unconfigured provider
produces. Misleading wording; do not go hunting for a missing wire.

**The `Model` picker's value came from the shipped Codex presets, not invented**: `gpt-5.6-luna`,
read straight out of `Codex - Python 3.gh`, which also confirmed that `Effort` is deliberately left
UNWIRED (Codex auto-places a picker on `Model` only, and an empty Effort means the model's own
default).

**A Python SyntaxError in a `-RunPythonScript` file raises a MODAL "Rhino 8 Exception Occured"
dialog that then silently blocks every later script.** The symptom is not an error — it is the next
three scripts producing no output file at all, which reads as the SendKeys channel having broken
again. Check for that dialog before blaming the channel, and dismiss it by title
(`WM_CLOSE`) as part of the send helper.
