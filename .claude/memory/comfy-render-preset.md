---
name: comfy-render-preset
description: "The Codex - Rhino to ComfyUI preset (viewport -> SDXL img2img -> picture back in the chat), why comfy-mcp must run in WSL rather than under uvx on Windows, and the RhinoApp.Idle trap that makes a Drive Rhino call look like it never ran."
metadata:
  node_type: memory
  type: project
---

Built 2026-09-07. `Files/PRESETS/User/Codex - Rhino to ComfyUI.gh` — capture the active Rhino
viewport, render it through the local ComfyUI (SDXL image-to-image) with a style the user types, and
hand the picture back into the conversation. 24 objects. **Run end to end through the preset's own
nodes** (no model, driven by Construct Tool Call + reflection): capture 288 KB → `upload_file`
`isError=False` → `run_workflow` `status: completed` in ~19 s on the 4060 Ti → `fetch_outputs` with
**`attachments=1`**, i.e. the render arriving as an image block.

Same conventions as [[blender-mcp-preset]] — Codex Model, stored source on every auto-placed Picker,
blank Panel on Schema — plus two structural firsts:
- **TWO tool nodes, so the Router grows a second output.** `CreateParameter` /
  `RegisterOutputParam(idx)` / `OnParametersChanged` / `VariableParameterMaintenance`, inserted at
  `Count - 1` so it lands BEFORE the trailing Feedback. It survives the archive round trip:
  reloaded outputs came back `['ComfyUI', 'run_rhino_script', 'Feedback']`, auto-renamed after the
  tools they reach, which is the cheapest proof both dispatch paths are wired.
- **FOUR backward hops, not three** — both tool nodes answer into `Router.Results`. That input is a
  LIST, so two Feedback → Feedback Collector pairs can both land on it; each hop still needs its own
  pair.

## comfy-mcp belongs in WSL, and `uvx comfy-mcp` on Windows does NOT work

This was the design fork, and the obvious branch is the wrong one. **comfy-mcp is a wrapper around
comfy-cli**, not a thin HTTP client: `server_info` wraps `comfy env`, `upload_file` wraps
`comfy upload`, `run_workflow` wraps `comfy run`. comfy-cli, the workspace and ComfyUI itself all
live in WSL. Launched on Windows under `uvx comfy-mcp`, the server STARTS and lists all 39 tools —
and then `server_info` and `upload_file` both return "Error executing tool …", because the thing
they shell out to is not there. **A tool list is not a working server**; that is the same shape of
mistake as the Claude Code provider advertising nothing.

So the entry runs it in WSL, and needs both of these:

```json
"comfy": { "command": "wsl.exe",
           "args": ["--", "env", "PATH=/home/<u>/.local/bin:/usr/local/bin:/usr/bin:/bin",
                    "/home/<u>/.local/bin/comfy-mcp"] }
```

`--` before the command, and an **explicit PATH** — `wsl.exe --` runs no login shell, so without it
`comfy` is not on PATH and every comfy-cli-backed tool fails exactly as it does on Windows. That one
difference cost a full diagnostic cycle. See the CORRECTION in [[blender-mcp-preset]]: `wsl.exe` is a
perfectly good stdio transport, Physalia's CRLF included.

**The path rule this forces.** Rhino writes on Windows, comfy reads in WSL, so the model must
translate `C:\X\Y` → `/mnt/c/X/Y` for every comfy argument and NOT for `run_rhino_script`. Verified:
`upload_file` accepts a `/mnt/c/...` path happily. The preamble states the rule explicitly because
the failure mode is "file does not exist" while you are looking straight at it.

## The RhinoApp.Idle trap — a call that looks like it never happened

`run_rhino_script` marshals its run to `RhinoApp.Idle`. **A driver script that then blocks the UI
thread — `Thread.Sleep`, or `.Wait()` on the task — prevents Idle from ever firing**, so the tool
call never executes and every observable says nothing happened: no file, no self-log, no error. It
cost two cycles because it is indistinguishable from a broken script. **Fire, let the driver script
EXIT, and read the result on a later run.** MCP calls are exempt (network I/O on a background
thread), which is why the Blender cube worked the first time with the same pattern.

Related, and worth having for any tool whose result the canvas cannot show: a manual batch mints no
Result signal, so **make the script log its own outcome to a file**. That is what finally produced
the real error.

## Facts about this stack that are easy to get wrong

- **`run_rhino_script` runs CPython 3.9, where `import System` does NOT give you `System.Drawing`.**
  You need `import clr; clr.AddReference("System.Drawing"); import System.Drawing`. The snippet that
  worked in my IronPython `-RunPythonScript` probe therefore did NOT work through the tool — two
  different engines, and the preamble has to carry the tool's flavour.
- `view.CaptureToBitmap(Size(w, h))` renders at the size ASKED FOR, not the on-screen size: a
  405x266 viewport still yields 1024x1024.
- **There is no local img2img template in the gallery.** `search_templates "image to image"` returns
  only paid partner-API graphs (Luma Photon). Hence the preset SHIPS a validated SDXL img2img graph
  at `Files/PROJECT_FILES/comfy-render/img2img-sdxl.json` — three fields to edit (node 10 image,
  node 6 prompt, node 3 denoise/seed) — which also means it travels with the repo, unlike anything
  in `%LOCALAPPDATA%`.
- **`fetch_outputs` has `inline_images`, default false.** True is the only reason the render reaches
  the conversation; without it the model reports a path the user cannot see.
- **`confirm_spend` exists and some templates/`partner_*` tools bill real money.** The preamble
  forbids passing it.
- ComfyUI was NOT running despite being reported as such; `comfy launch --background` starts it
  (comfy-cli remembers the workspace) and it does not survive a reboot. `server_info` is the check.
- SDXL at denoise ≥0.65 garbles any TEXT in the image — visibly, in the first test render. Fine for
  massing, bad for a titled sheet; the preamble gives 0.3–0.45 / 0.55–0.65 / 0.75+ guidance.
