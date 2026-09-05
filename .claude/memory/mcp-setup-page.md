---
name: mcp-setup-page
description: "The chat window's MCP setup flow as rebuilt 2026-09-04 — landing page, pasted-CLI-command parser, Test connection — and the fact that CLAUDE.md's MCP section still describes the old one."
metadata: 
  node_type: memory
  type: project
  originSessionId: d99ac3f2-6497-4903-b063-925646c7cd5c
  modified: 2026-09-04T22:45:02.768Z
---

Rebuilt 2026-09-04 across four commits: `4913b1d` (transport labels), `50a2071` (test connection),
`6d4f473` (landing page + parser), `f623d5b` (return to the list after saving). Driven by trying to
connect Adobe Illustrator's built-in MCP server and finding the form easy to fill in wrongly.

**⚠ CLAUDE.md's "MCP — Physalia is a CLIENT" section is STALE on all of this.** It still describes a
single form with "Save & sign in" and states that the sign-in flag rides on the save verb because the
host must read the entry back off disk. That was true and is no longer the whole picture. Trust this
note and the code over it, and fold it back into CLAUDE.md when convenient.

## The flow now
**Add a server** opens a landing page with two cards. **Connect automatically** takes a setup command
pasted from the server's own instructions. **Connect manually** is the old form. The manual form's
transport toggle reads **URL | Command** (not Local | Remote — the split is subprocess-vs-HTTP, and a
desktop app on loopback is a URL server on your own machine), URL leads and is the default.

Both modes end with **Test connection** on the left, then **Save & connect**, then **Cancel**. The
plain Save button is gone: `WriteMcpEntry` writes the entry BEFORE connecting and reports the
connection separately, so a server that is not running yet was always being saved anyway.

- **Test connection writes nothing** and leaves the form open — the point of a test is to fix what it
  reports. **Save & connect returns to the list**, where the result banner and the new row appear.
- Host verbs: `testmcpserver` (draft, built in memory), `testmcpcommand` / `savemcpcommand` (raw
  pasted text, parsed host-side). Shared helpers: `BuildMcpDefinition(payload, remote, expand)`,
  `WriteMcpEntry(definition, replacing, connect)`, `ConnectAndReport(definition, name, note)`.
- The `expand` flag carries the one rule that differs between the paths: an EDIT keeps `${VAR}`
  verbatim (writing the resolved token into the file is the leak the reference exists to prevent), a
  CONNECTION resolves it. Values only, never keys.

## McpCommandParser (Core, pure, 18 tests)
`Physalia.Core/Mcp/McpCommandParser.cs` parses `claude mcp add …` and `codex mcp add …`. The page
sends RAW TEXT and the host parses — a TypeScript copy would drift from the thing actually written to
the file. The Claude Code / Codex toggle picks the EXAMPLE only; the grammar is **detected**, so
pasting one under the other option still works (there is a test for it).

- **`--scope` is read and discarded.** It names Claude Code's settings FILE (user/project/local), not
  an OAuth scope; copying it over would ask the authorization server for a scope called "user".
- **A literal credential stays literal.** Rewriting it to `${VAR}` yields an entry resolving to
  nothing until the user sets a variable nobody told them about, which defeats pasting.
  MCP_SERVERS.YAML is gitignored and a preset carries only a server's NAME, so it travels nowhere.
  Codex's `--bearer-token-env-var VAR` takes the value its own `set "VAR=…"` prelude assigned, and
  keeps the `${VAR}` reference only when no prelude carried one.
- Two parser bugs the tests caught before the UI saw them: a multi-line paste's `\` continuation
  became the server's NAME, and `claude mcp add x npx -y pkg` lost the package because `-y` was read
  as a flag this parser owned. **Flag reading stops once the name and its url-or-command are in
  hand** — everything after belongs to the launched program.

## Operational traps
- **`McpServer.ConfigPath` is next to the ASSEMBLY.** The running plug-in reads
  `bin/Debug/net7.0-windows/Files/MCP_SERVERS.YAML`, not the repo's `Files/`. Editing the repo copy
  changes nothing for a running Rhino.
- **The bridge is a subprocess**, so a bridge-only fix needs no `.gha` rebuild — retrying the
  connection picks it up even while Rhino holds the `.gha` lock. See [[mcp-bridge-chunked-body]].
- The `.gha` copy step fails whenever Rhino or Visual Studio has the plug-in loaded; the compile and
  the UI bundle still succeed, so check `bin`'s timestamp rather than trusting "Build succeeded".

Related: [[mcp-integration]], [[mcp-bridge-chunked-body]], [[settings-ownership]].

**2026-09-04: the server list moved to `%LOCALAPPDATA%/Physalia/MCP_SERVERS.YAML`** (out of `Files/`).
Servers are added through the chat window, so it is machine state; in the install folder a plug-in
update could overwrite it and every account on the box shared one credential list. `McpServer.ConfigPath`
MOVES an existing `Files/` copy there once (moves, not copies — two would drift). The `.example`
template still ships in `Files/`. It stays readable YAML rather than joining the encrypted store:
a server entry is mostly a command, its args and a URL, and `${VAR}` exists so the credential part
need never be written down — same reasoning as `providers.json`. See [[model-api-credentials]].
