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


## What shipped (2026-09-02)

- **`src/Physalia.GH/Generation/RhinoScriptRunner.cs`** — the engine wrapper.
  `RhinoCode.Languages.QueryLatest(LanguageSpec.Python3)` (with one forced `WaitStatusComplete` +
  retry, because Python 3 loads lazily and a cold query returns null) then `ILanguage.CreateCode` +
  `Code.Run(RunContext)`. `RunContext` is constructed with `defaultOutputStream/defaultErrorStream:
  true` **and then assigned our own `MemoryStream`s** — the flags make the engine allocate, the
  assignment points it at buffers we can read. Returns a `ScriptOutcome` record and **never throws
  for a fault in the script**: `CompileException` (carries `.Diagnosis`), `ExecuteException`
  (carries `.Position` + `.StackTrace`) and anything else all come back as data.
- **`src/Physalia.GH/Components/LlmTools/RunRhinoScript.cs`** — the node. `RunsAsync => true`,
  marshalled to `RhinoApp.Idle` with the same one-shot handler + `CancellationTokenRegistration`
  pattern as `TakeSnapshot.CaptureOnIdleAsync`. `Last Script` output published from `OnSolveEnd`.
  ComponentGuid `2F6A9C31-84D7-4B05-9E13-5C7A0D2E8B64`.

**Three decisions worth not re-litigating:**
1. **Undo is owned explicitly** (`doc.BeginUndoRecord`/`EndUndoRecord` around the run) with
   `RunContext.RecordDocumentUndo = false`, not delegated to the engine — so the label is ours and
   the behaviour does not depend on an engine default that was never verified.
2. **The timeout bounds waiting for Idle to ARRIVE, not the script.** Once the handler runs the
   script owns the UI thread and no token can take it back (a managed thread cannot be aborted). A
   runaway script blocks Rhino exactly as it would in the Script Editor. Do not "fix" this with a
   longer timeout or a worker thread — RhinoCommon mutation off the main thread is worse.
3. **Object count before/after is reported on every path including failure.** A script that raises
   half way through has already applied what it did; a model that assumes otherwise retries and
   doubles the geometry.

**Not verified, needs a live Rhino test** — the whole point is behaviour the command line cannot
show:
- **Does the Python 3 engine actually write to `RunContext.OutputStream`?** This is the load-bearing
  assumption. If `print` does not land in the buffer, the "print IS the read-back" design fails and
  a `get_context`-style tool becomes necessary after all.
- Whether `BeginUndoRecord` really collapses a multi-object script into one Ctrl+Z.
- Whether `RhinoApp.Idle` fires promptly while a GH tool round is in flight (TakeSnapshot's
  precedent says yes).
- `RunRhinoScript.png` does not exist — the node wears the fallback brain icon. Prompt is recorded
  in `planning/component-icon-prompts.md` under "Pending — no icon yet".
