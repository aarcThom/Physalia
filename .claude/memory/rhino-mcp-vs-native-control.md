---
name: rhino-mcp-vs-native-control
description: Why Physalia should control Rhino natively via Rhino.Runtime.Code rather than connecting to McNeel's Rhino MCP server, with the measured facts about that server.
metadata:
  type: project
---

2026-09-02. Investigated shipping McNeel's Rhino MCP as a Physalia preset; concluded a **native
`run_rhino_script` LLM tool is the better build**. Not yet built — the rename below is all that
shipped.

**What the Rhino MCP actually is** (read from `RhinoAI-rhino-9.x` source, then verified live):
- The plugin auto-starts a Kestrel listener **inside Rhino** on first `BeginOpenDocument`
  (`PlugInLoadTime.AtStartup`), port **10500**, one per open doc. No `MCPStart` needed.
- Endpoint is `http://localhost:<port>/` — **plain HTTP POST, JSON-RPC 2.0, one request one
  response**. No SSE, no `Mcp-Session-Id`, no auth. Their router's comment: "Stateless = true, so no
  initialize handshake is required". A second `/agent` endpoint honours the user's `DisabledTools`
  but exposes `InPanelOnly` tools (`ask_user`, needs their panel) — so use `/`.
- **Probed live 2026-09-02**: `initialize` returns `protocolVersion 2024-11-05`,
  `serverInfo.name "rhino-mcp"`, version 0.2.1.0.
- `rhino-mcp-router.exe` is a stdio MCP server OUTSIDE Rhino that spawns/adopts Rhinos and proxies to
  that port. **Unshippable in a preset**: `RouterMcpConfig.RouterPath` is version-stamped into the Yak
  package dir (`…/Rhino-MCP-Platform/0.2.1-wip/router/win-x64/`). The user's own `.claude.json` has
  both a `0.1.5` and a `0.2.1-wip` entry; the stale one is a dead MCP server in their Claude Code.
- Discovery exists: `%LOCALAPPDATA%\McNeel\rhino-mcp\listeners\<pid>-<port>.json`, **re-dropped every
  15 s** by a heartbeat, `.gone` tombstone on clean close. Routers delete them as they scan.

**Why native wins.** `Physalia.GH.csproj:117` **already references `Rhino.Runtime.Code`** (for
`GhPythonBridge`). That is the ScriptEditor's own engine and it runs standalone:
`RhinoCode.Languages → QueryLatest(LanguageSpec.Python3) → CreateCode(src) → TryRun(RunContext, out
Diagnosis)`. `RunContext` gives real `OutputStream`/`ErrorStream`, typed `Inputs`/`Outputs`, and
**`RecordDocumentUndo`** — a whole model action as one undo step. The MCP's `RunPythonTool` is 46
lines of temp-file + `RhinoApp.RunScript("_-ScriptEditor _Run")` + scraping
`CapturedCommandWindowStrings` + string-matching "Traceback", and its description has to warn the
model "Do NOT trust `scriptcontext.doc`". That crudeness IS the out-of-process tax.

**Size**: their whole non-GH tool surface is 1,588 lines, of which 424 is their `AskUser*` panel UI
and 234 is `get_viewport_image` (Physalia's `take_snapshot` already does more). Native plan is
~600-800 lines vs ~3,000 for the MCP client work (transport split + port discovery + built-in server
entry + per-tool advertise selection) — and that still depends on a third-party plugin being
installed.

**The shape as built** (see [[settings-ownership]] for why per-node advertise matters):
- `run_rhino_script` — LLM Tool, **not a transmitter**. Physalia's own rule: "side-effect-ness is not
  what makes a transmitter; direction across the harness boundary is." Model's control flow, must
  return in-turn. It also has nowhere to put a drag arrow — Rhino has no drop target.
- A Rhino Doc grounder for standing facts (units, layers, counts, camera). Needs its OWN watch on
  `RhinoDoc.AddRhinoObject`/`DeleteRhinoObject`/`ModifyObjectAttributes`, throttled, because
  **nothing in GH expires a component when the Rhino document changes** — same problem
  `CanvasStateGrounder` solves with a `SolutionEnd` watch.
- **No `get_context` tool.** With a real `OutputStream`, `print` IS the read-back, which is exactly
  why the MCP needs a dedicated 156-line context tool and Physalia would not.
- Do NOT advertise their GH1/GH2 tools if the MCP route is ever taken — they duplicate CompTx without
  the Schema Validator / Required Input Check / Fidelity Check guardrails.

**Keep the MCP client.** It is how Physalia reaches Notion, filesystem, GitHub. Just do not use it to
reach the room Physalia is standing in. One thing the router alone can do: drive a *different* Rhino
(it spawns 8 and 9 side by side) — niche, for upgrade/downgrade work.

**Shipped this session**: the LLM tool node `RhinoGeometryTool` renamed "Rhino Geometry" →
**"Create/Ref. Rhino Geometry"**, display name only, `ComponentGuid 7D3F1A94-…8A21` pinned,
class/file/nickname untouched — same discipline as the Set Script I/O rename. See
[[component-rename-plainspoken]].

## Two live findings about their server, both worth remembering

**`_router_quit_app` is advertised on the PUBLIC `/` endpoint.** So are
`_router_spawn_listener` and `_router_close_listener`. If Physalia ever connects to this server
directly and advertises everything `tools/list` returns, the model can close the user's Rhino
mid-session. **Any direct connection must filter the `_router_` prefix.** Going through the router
hides them (it exposes `spawn_slot`/`close_slot`/`list_slots` instead), so this is specific to the
direct path.

**Their router can DoS itself on stale announcements.** A `rhino` MCP server in Claude Code timed
out at 30s. Cause: ~300 stale `listeners/*.json` files, because `RhinoManager.ScanAnnouncements`
gates staleness on `IsPortListening(ann.Port)` — the **port, not the pid**. With a live Rhino on
10500 every dead-pid file for 10500 passed the liveness check and was adopted as its own slot, one
SQLite write each, synchronously before `host.RunAsync()` could answer `initialize`. Draining the
directory fixed it; the router then connects in 0.28s. Upstream fix would be to check the pid too and
to get adoption off the handshake's critical path.

## Shipped in the same session

The LLM tool node `RhinoGeometryTool` renamed "Rhino Geometry" -> **"Create/Ref. Rhino Geometry"**,
display name only, `ComponentGuid 7D3F1A94-...8A21` pinned, class/file/nickname untouched — same
discipline as the Set Script I/O rename. Two Rhino-facing tools now sit side by side and the old name
no longer said which was which. See [[component-rename-plainspoken]].
