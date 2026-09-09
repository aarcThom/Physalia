# MCP — Physalia is a client

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## MCP — Physalia is a CLIENT (built 2026-08-27)

Physalia connects to **other people's MCP servers**; it is not one. An MCP connection is **NOT a
transmitter**: a transmitter is the harness *outlet*, driven by the *pipeline's* control flow and
writing into the user's GH document. An MCP call is driven by the *model's* control flow and must
return inside the same assistant turn, so it belongs to the **LLM Tools** tier. Side-effect-ness is
not what makes a transmitter; direction across the harness boundary is.

MCP's three primitives land on three different tiers: **tools** → LLM Tools (built); **resources** →
Grounding (not built); **prompts** → System Prompt's `Additional Prompt` (not built).

### THE SDK CANNOT RUN INSIDE RHINO — measured, and no packaging change fixes it
- **Rhino 8 runs on the .NET 8 shared runtime (8.0.30)** even though the plug-in targets net7.0.
- **`System.Text.Json` is served by the SHARED FRAMEWORK** (`Microsoft.NETCore.App\8.0.30`, v8.0.0.0)
  — *not* Rhino's own `Program Files\Rhino 8\System\System.Text.Json.dll` (7.0.0), and *not* any
  copy deployed beside the `.gha`. It is a framework assembly, so **the app-local copy is never
  consulted**. No binding redirect, no ILRepack denylist entry, nothing can change this.
- `ModelContextProtocol.Core` has no net7.0 asset → net7.0 resolves the **netstandard2.0** one, whose
  dependency group demands `System.Text.Json 10.0.10` + `Microsoft.Extensions.AI.Abstractions`. The
  cctor of `Microsoft.Extensions.AI.AIJsonUtilities` calls `JsonElement.Parse(ReadOnlySpan<byte>,
  JsonDocumentOptions)` — a **.NET 10** addition — and throws `MissingMethodException`. Downgrading
  does not help: the oldest version on the feed already wants the 10.x line.
- **The SDK's TYPES load and construct fine; only the JSON layer is dead** — which is total, MCP being
  a JSON-RPC protocol. A "does it load?" test reports success. **Any future probe of a third-party
  package in Rhino must EXECUTE a real code path, and must isolate each stage behind a
  `[MethodImpl(NoInlining)]` method invoked through a delegate**, because `TypeLoadException` /
  `MissingMethodException` fire when the *enclosing* method is JIT'd and would sail past every
  `catch` in `SolveInstance`.

### The shape that follows: one transport in-process, a bridge for the rest
- **`Physalia.Core/Mcp/`** implements the **stdio transport only** — `McpSession` (JSON-RPC 2.0 over a
  warm subprocess, background read pump, ids correlated through `TaskCompletionSource`s) and
  `McpConnections` (pool keyed by `McpServerDefinition.Identity`, idle reaper, `ProcessExit`
  teardown). Same lifecycle contract as the CLI providers, **zero new package references**.
- **`Physalia.McpBridge`** (net8.0 exe, staged to `Bridge/Physalia.McpBridge.exe`) reaches **remote /
  OAuth-protected** servers. It is a **relay, not a second MCP implementation**: stdin → the SDK's
  `HttpClientTransport` → stdout, verbatim. All MCP semantics stay in Core. net8.0 because Rhino
  already brings that runtime, and it **pins `System.Text.Json 10.0.10` explicitly** — an ordinary
  app resolves from its own `deps.json`, so there the pin actually wins. **stdout is the protocol;
  every diagnostic goes to stderr.** Never merged by ILRepack (it lives in a subfolder;
  `RepackInputDll` only globs `$(TargetDir)` itself).
- A `url:` entry launches the bridge transparently; a missing bridge is reported only when a remote
  server is actually asked for, so a stdio-only install is fully usable.

### Component + config
- **`McpServer`** (`LlmTools/`) — one node per connection, one generic class, **nothing per service**.
  It is **the only node that advertises MANY tools**, which is why `LlmToolComponentBase` grew
  `Definitions` (plural, virtual; `Definition` stays the override for every other node) and the Tool
  output became `GH_ParamAccess.list`. Tool names are **namespaced `{server}__{tool}`** and sanitized
  to `^[a-zA-Z0-9_-]{1,64}$` — two servers exporting `search` would otherwise collide on one Router
  key; `LocalName` maps back through the discovered set, since sanitizing is lossy.
- **Router dispatch matches a SET**: `ToolOutputSlot(OutputName, ToolNames)`, and an unmatched call is
  told the **tool** names, never the output names. An output serving one tool is still named after
  it; one serving many takes the node's nickname, de-duplicated because the name is the dispatch key.
- **`Router.InspectConnection` must read `LlmToolComponentBase.AdvertisedDefinitions`, NEVER the Tool
  output's `VolatileData`** (fixed 2026-09-03 off a signal trace; it read VolatileData originally).
  Volatile data is cleared at the start of every solution and refilled only when the node itself
  re-solves — but a signal-driven dispatch expires the **Router**, not the tool node upstream of it.
  So `SyncToolOutputNames`, which also runs at SolutionEnd just after the node solved, saw the whole
  set, while `DispatchToolCalls` in the next scheduled solve saw NOTHING and fell back to
  `new[] { output.NickName }`. **That fallback is right by coincidence for a one-tool node** — the
  output has already been named after its tool — which is why this survived until MCP, the one node
  advertising many: there the output is named after the NODE, so every call was answered
  *"The tool `notion__notion-fetch` does not exist. The available tools are: MCP Server."* — handing
  the model an output name, the exact thing `ToolDispatchRound` takes care never to do. **The pure
  layer was innocent and fully tested throughout** (`McpDispatchSlotTests` even asserts that error
  names tools, not outputs), so no Core test could have caught it: the policy was correct and the GH
  adapter fed it wrong data. The general lesson for any component reading a PEER's output: within a
  signal-driven solve that peer has not re-solved, so ask the component, not the solver.
- **`%LOCALAPPDATA%/Physalia/mcp-servers.json`** holds the servers (`McpServerStore`). **The YAML is
  gone entirely** (2026-09-05) — file, `.example` template, the in-place `McpConfigEditor` that
  preserved its comments and ordering, and the JSON-form read-only refusal. All of that existed to
  protect hand-authoring that stopped happening the moment the chat window's setup page took over;
  what the machine writes needs a shape, not commentary. Same argument that killed
  `API_KEY_CONFIG.YAML`. An older YAML (beside the plug-in, or already relocated) is imported once
  and then deleted — but **only when something actually parsed out of it**: deleting a file we failed
  to read is a deletion, not a migration.
  **What is stored is the standard `mcpServers` block**, not a Physalia envelope — no version field,
  no wrapper — so a `claude_desktop_config.json` still pastes in whole (`Import`) and the file can be
  lifted out and used elsewhere. `McpServerLibrary` is now pure parsing only (both shapes, since a
  README snippet may be either); the store owns the file. **Plain, not encrypted**: an entry is
  mostly a command, its args and a URL, and `${VAR}` exists so a credential need never be written
  down — the same reasoning as `providers.json`. **`Read()` expands `${VAR}`, `ReadRaw()` does not**,
  and the setup page MUST use `ReadRaw` — populating a form from expanded values and saving it back
  bakes the resolved secret into the store the reference existed to keep it out of.
- **Six recognised keys, in two transport-shaped halves** — `command`/`args`/`cwd`/`env` for a local
  stdio server, `url`/`headers`/`scope` for a remote one. The local half is what nearly every
  published server is, so anything offering "a URL and a key" would refuse most of the ecosystem.
  `headers` is where a **static bearer token** for a remote server goes and `scope` narrows the OAuth
  sign-in; both are ignored on a local entry, whose credentials belong in `env`. Both are folded into
  `McpServerDefinition.Identity` for the same reason `env` is — a warm bridge process authenticated
  with the old token must not serve the new definition — and both reach the server ONLY through the
  bridge's `--header Name=Value` / `--scope` arguments, added in `McpSession.StartProcess`'s remote
  branch. Most hosted servers need neither: the bridge signs in over OAuth, so blank is the normal
  case, not the exception.
- **OAuth tokens are cached on disk by the bridge, and that is what makes an early sign-in worth
  anything.** `ClientOAuthOptions.TokenCache` was unset, so the SDK kept tokens *with the transport*
  — and the bridge is short-lived by design (`McpConnections` reaps an idle session after ten
  minutes; every Rhino restart kills the pool), so the user faced a browser sign-in on nearly every
  cold start. `FileTokenCache` (bridge-side) stores them under
  `%LOCALAPPDATA%/Physalia/mcp-auth/<sha256 of endpoint+scope>.tok`, **DPAPI-encrypted with
  `DataProtectionScope.CurrentUser`** on Windows and plaintext-with-owner-only-mode elsewhere (DPAPI
  is Windows-only). **The `ClientId` must be persisted alongside the refresh token, not just the
  tokens** — it is what the dynamic client registration produced, and the SDK restores it from the
  cache so a cold start can redeem the refresh token without re-registering *and without prompting*;
  dropping that field silently reintroduces the sign-in it was meant to remove. `GetTokensAsync` is
  on the request hot path ("invoked for every request"), so the disk read and DPAPI decrypt happen
  ONCE and are held in memory. Every failure path returns "no cached token" rather than throwing — an
  unreadable cache is exactly as recoverable as no cache, while an exception would take down a
  connection that was otherwise fine. The file name is a HASH so a directory listing does not leak
  which services the user has connected to.
- **Two Mac-port items live in this stack, and only one is a break** (noted 2026-09-03; memory note
  `mac-port-mcp-gaps`). **`McpServer.BridgeExecutable()` hardcodes `Physalia.McpBridge.exe`**, but a
  net8.0 console app's apphost on macOS is `Physalia.McpBridge` with **no extension** — so the probe
  fails, the method returns null, and EVERY remote server reports the bridge missing on a build that
  is otherwise healthy. Local stdio servers keep working, which is what will make it look like a
  server-specific fault rather than a platform one; it needs to probe both names (or fall back to
  `dotnet Physalia.McpBridge.dll`). The DPAPI token cache above is the *safe* one — it already
  branches to plaintext + owner-only mode off Windows, and the proper Mac answer is the Keychain, so
  do not "fix" it by dropping encryption on Windows to make the platforms match. Already Mac-safe and
  not worth re-auditing: `McpExecutable.Resolve` guards PATHEXT behind `IsWindows`, `CopyMcpBridge`
  globs `**\*` so it stages whatever the apphost is called, `LocalApplicationData` maps to
  `~/.local/share`, and `UseShellExecute = true` opens a browser via `open`.
- **`McpServer` re-reads the file when it CHANGES** (`Store.RevisionStamp`), not only when its cached
  list is empty — see the HTTP APIs section, which fixed this node and `ApiCall` together. The reload
  resets `_discovered`/`_listed` **only when the picked server's `Identity` changed**, since editing
  an unrelated entry must not drop a live session's tool list.
- **The chat window's Home screen edits this file** ("Configure MCP connections", also on the header
  menu). Two invariants there, both load-bearing. (1) **The file is EDITED, never regenerated** —
  `McpConfigEditor` replaces only the edited entry's line range, so the shipped commentary, the
  user's own notes, the entry order and the file's indentation style all survive; an entry's span
  deliberately stops before any trailing blank/comment lines, because a comment describes the entry
  BELOW it and deleting a server must not take its neighbour's documentation. (2) **The editor reads
  values UNEXPANDED** (`McpConfigEditor.ParseRaw` → `McpServerLibrary.Parse(expandEnvironment:
  false)`). Populating the form from expanded values and saving would write the resolved token into
  the file that `${VAR}` existed to keep it out of — a silent credential leak on the user's next
  unrelated edit. **The JSON form is read but never written**: it is a config shared with another MCP
  host, so `DescribeWriteBlock` refuses it and the page goes read-only rather than converting it.
- **The page also SIGNS IN**, which is the point of configuring a remote server there at all: "Save &
  sign in" (and a connect button on each list row) calls `McpConnections.GetAsync` + `ListToolsAsync`
  right then, so the browser handshake happens during setup instead of on the first solve of a node
  the user has not placed yet. It doubles as a connection test — what comes back is the tool count,
  so a wrong URL or an unresolvable command is caught immediately. Three details: the sign-in flag
  rides **on the save verb** (`?signin=1`) rather than being a second call from the page, because
  connecting has to read the entry back off disk and the page cannot know when the write landed; the
  timeout is **five minutes, not the node's two**, because a consent screen runs at human speed; and
  this is the ONE place on the page that reads values **expanded**, since it is a connection rather
  than an edit and a `${VAR}` must resolve to the credential it names.
- **`McpExecutable.Resolve` is load-bearing on Windows.** Practically every published config says
  `command: npx`, those are `.cmd` shims, and `CreateProcess` does **not** apply `PATHEXT` — bare
  `npx` throws `Win32Exception`. PATHEXT variants are tried **before** the bare name, because npm
  installs an extensionless Unix shell script next to `npx.cmd` and picking it yields "not a valid
  application for this OS platform". Both failures were hit live.
- Physalia declares **no `sampling` and no `elicitation`** in the handshake: sampling would let a
  third-party server spend the user's tokens through an LLM Call with nothing on the canvas recording
  it. A server-initiated request is still **answered** (`-32601`) — an unanswered one blocks that side
  forever, the same trap as the Codex app-server.

Verified live against `@modelcontextprotocol/server-everything` (stdio and Streamable HTTP through
the bridge): connect, `tools/list`, `tools/call`, image attachments, pooling, teardown. **Not yet run
inside Rhino**, and the **OAuth flow is unverified** — it needs a real protected server.

---

