---
name: mcp-integration
description: "MCP client design for Physalia, and the measured proof that the official C# SDK cannot run in-process inside Rhino"
metadata: 
  node_type: memory
  type: project
  originSessionId: b7987b0d-1a05-49dc-88ed-e291cd86d533
  modified: 2026-08-28T05:53:52.148Z
---

Physalia is to become an MCP **client** (host), calling other people's MCP servers — not an MCP server.
Design settled AND BUILT 2026-08-27. Authoritative detail now lives in **CLAUDE.md ('MCP — Physalia is a
CLIENT')**; this note keeps the reasoning and the traps.

**An MCP connection is NOT a transmitter.** A transmitter is the harness *outlet* — it writes into the
user's GH document, driven by the *pipeline's* control flow. An MCP call is driven by the *model's*
control flow and must return inside the same assistant turn, so it belongs to the LLM Tools tier
(`LlmToolComponentBase` → Router → Feedback → Collector). Side-effect-ness is not what makes a
transmitter; direction across the harness boundary is. See [[harness-subdocument]], [[tools-in-use-component]].

MCP's three primitives split across three existing tiers: **tools** → LLM Tools; **resources** →
Grounding (a grounder emitting `GH_Grounding`); **prompts** → System Prompt's `Additional Prompt`.

## Settled shape
- **One generic `McpServer` component class, placed once per connection.** Not one class per service
  (no catalog to maintain), not one node for all servers (a dead server would take down the rest and
  the canvas would stop showing what the model can reach).
- **Server definitions live in `Files/MCP_SERVERS.YAML`** (+ `.example`), in the **standard `mcpServers`
  block** every other host uses, so users paste configs straight from any README. Physalia ships ZERO
  server definitions. Picked by name through a **Picker** — the `Files/CLUSTERS` + `clusters.json` and
  `PresetLibrary` match-by-name idiom.
- **Why external, not on the node:** MCP configs carry tokens in `env`. On the node they would
  serialize into the `.gh` and ship inside a preset — a credential leak through the preset mechanism.
  Same discipline as `API_KEY_CONFIG.YAML` / the `GH_ModelConfig.Write/Read` no-ops. See [[settings-ownership]].
- **On the node** (so it ships in the preset): which server is picked, and a checklist of which of its
  tools are advertised — same shape as `ComponentCatalogGrounder`'s multi-select via `SettingArchive`.
- **Router needs set-matching**: today an output's name is the single `LlmToolDefinition` of the node
  wired to it (`Router.InspectConnection`); an MCP node advertises N, so an output must match a *set*.
- **Decline `sampling` and `elicitation`** in the `initialize` handshake. Sampling would let a
  third-party server spend the user's tokens with no node on the canvas recording it.
- Namespace tool names per server (`filesystem__read_file`) — `Router._dispatched` is keyed by nickname,
  so two servers exporting `search` would silently misroute. Sanitize to `^[a-zA-Z0-9_-]{1,64}$`.
- Handle `notifications/tools/list_changed` — re-list, which flows through `ToolsInUse.Signature` and
  expires the Conversation Log by itself.

## MEASURED 2026-08-27: the official C# SDK cannot run in-process in Rhino
Probed with a throwaway `McpLoadProbe` GH component (since reverted). Two facts worth more than the
verdict, because they invalidate the obvious mental model:

1. **Rhino 8 runs .NET 8.0.30, even though Physalia targets net7.0** — roll-forward onto the .NET 8
   shared runtime.
2. **`System.Text.Json` is served by the SHARED FRAMEWORK** (`Microsoft.NETCore.App\8.0.30`, v8.0.0.0)
   — NOT Rhino's own `Program Files\Rhino 8\System\System.Text.Json.dll` (7.0.0), and NOT the copy
   Physalia deploys loose beside the `.gha`. It is a framework assembly, so **the app-local copy is
   never consulted. You cannot fix this by shipping a different DLL** — no binding redirect, no
   ILRepack denylist entry, nothing.

`ModelContextProtocol.Core` has no net7.0 asset, so net7.0 resolves the **netstandard2.0** one, whose
dependency group demands `System.Text.Json 10.0.10` + `Microsoft.Extensions.AI.Abstractions 10.8.3`.
Result inside Rhino:

```
MissingMethodException: JsonElement.Parse(ReadOnlySpan<Byte>, JsonDocumentOptions)
  thrown from the cctor of Microsoft.Extensions.AI.AIJsonUtilities
```

`JsonElement.Parse` (static) is a **.NET 10 addition**. The throw comes from the transitive
`Microsoft.Extensions.AI` dependency, not MCP's own code. **Downgrading does not help** — the oldest
version on the feed (1.2.0) already wants Extensions.AI 10.4.1 / STJ 10.0.5.

**The SDK's TYPES load and construct fine** (`McpClientOptions`, `Implementation`,
`StdioClientTransportOptions`, `StdioClientTransport` all OK). Only the JSON layer is dead — which is
total, since MCP is a JSON-RPC protocol. A naive "add the reference and see if it loads" test would
have reported success. **Any future probe of a third-party package in Rhino must EXECUTE a real code
path, and must isolate each stage behind a `[MethodImpl(NoInlining)]` method invoked through a
delegate** — `TypeLoadException`/`MissingMethodException` fire when the *enclosing* method is JIT'd,
so naming the type directly in `SolveInstance` throws before any `catch` in it can run.

Loose (`-p:RepackGha=false`) is the more permissive configuration, so a loose failure needs no merged
re-run. Unrelated to the JsonSchema.Net scar in the `RepackGha` target, which is about ILRepack's
typeref rewrite, not version selection. See [[physalia-repo-gotchas]], [[ilrepack-release-double-merge]].

## BUILT — what exists now
`Physalia.Core/Mcp/` (McpServerDefinition, McpServerLibrary, McpExecutable, McpSession,
McpConnections, McpToolCallResult) + `Components/LlmTools/McpServer.cs` + `Physalia.McpBridge`
(net8.0 relay) + `Files/MCP_SERVERS.YAML(.example)`. `LlmToolComponentBase` gained plural
`Definitions`; `ToolDispatchRound` gained `ToolOutputSlot` set-matching. 511 Core tests pass (22 new).

**Two Windows traps, both hit live and both would break the most common config line in the
ecosystem** (`command: npx`): `CreateProcess` does not apply `PATHEXT`, so a bare `npx` throws
`Win32Exception`; and PATHEXT variants must be tried BEFORE the bare name, because npm installs an
extensionless Unix shell script beside `npx.cmd` and picking it gives "not a valid application for
this OS platform". Hence `McpExecutable`.

**Verified live** against `@modelcontextprotocol/server-everything` via a scratch console harness,
both stdio and Streamable-HTTP-through-the-bridge: connect, tools/list, tools/call, image
attachments (4033-byte PNG intact), pooling identity, teardown, and a clear error for a remote server
with no bridge. **OAuth is still UNVERIFIED** — it needs a real protected server.

**Update 2026-09-04.** A second real remote server — Adobe Illustrator's built-in one, static bearer
token, 47 tools — now works through the bridge, but only after three defects were fixed; the
chunked-request-body one affects EVERY remote server, so treat the earlier server-everything result
as necessary and not sufficient. See [[mcp-bridge-chunked-body]]. The chat window's setup flow was
rebuilt at the same time (landing page + a parser for pasted CLI setup commands) and **CLAUDE.md's
MCP section is stale on it** — see [[mcp-setup-page]].

## Consequence: the bridge is the plan
Physalia implements **only the stdio MCP client** in-process (~300 lines on the `CodexProvider` /
`CodexSession` chassis — pooled `ConcurrentDictionary`, idle reaper, `ProcessExit → DisposeAll` — see
[[codex-provider]]), with **zero new package references**. Remote/OAuth servers arrive through a
**separate bridge process** hosting the real SDK on **net8.0** (the runtime Rhino already brings), with
`System.Text.Json 10.0.10` pinned explicitly — an ordinary app resolves from its own `deps.json`, so
there the pin wins where beside the `.gha` it cannot. It speaks MCP over stdio
back to Physalia. `url:` entries in the YAML spawn the bridge transparently; the user pastes the same
standard config either way. OAuth's browser redirect and loopback listener then live outside Rhino,
which is where they belong anyway.

## 2026-09-07 — RUN INSIDE RHINO at last, and the OAuth flow driven to the consent gate

Both gaps CLAUDE.md flagged ("Not yet run inside Rhino", "the OAuth flow is unverified") are now
closed as far as they can be without a human pressing Allow.

**The bridge works inside Rhino.** `McpServer.BridgeExecutable()` resolved to
`bin/.../Bridge/Physalia.McpBridge.exe`, and a `url:` entry against a local
`@modelcontextprotocol/server-everything streamableHttp` (port 3001, path `/mcp`) reported
**`connected, 13 tools`**, namespaced `everything-http__*`, sitting on the same canvas as the stdio
`blender` node with 28. A `tools/call` round trip came back (`Echo: physalia bridge round trip`), and
the control — an unknown tool — returned `MCP error -32602: Tool no-such-tool not found`, so errors
propagate rather than being swallowed. Two bridge processes ran side by side, one per remote entry.

**OAuth reaches the browser and the whole handshake is visible in the node's Status.** Against
`https://mcp.notion.com/mcp` the status output spelled out the chain:

```
bridge: GET https://mcp.notion.com/mcp answered 401, so the standalone stream is off for this session.
bridge connected to https://mcp.notion.com/mcp (auth: OAuth, standalone GET stream: no)
bridge: opening a browser to sign in - https://mcp.notion.com/authorize?client_id=<issued>&
  redirect_uri=http://127.0.0.1:60289/callback&response_type=code&code_challenge=<...>&
  code_challenge_method=S256&state=<...>&resource=https://mcp.notion.com/mcp&scope=default
```

Everything worth checking is in that URL: **dynamic client registration SUCCEEDED** (the `client_id`
was issued at runtime and configured nowhere), **PKCE S256**, a **loopback callback**, the **RFC 8707
`resource` indicator** MCP requires, and `scope=default` matching what
`/.well-known/oauth-protected-resource` advertises. A Chrome window titled "Notion - Log in" opened.

**Still unverified, and only a human can close it:** the code→token exchange, the `FileTokenCache`
write (DPAPI-encrypted, filename = sha256 of endpoint+scope, so a directory listing does not leak
which services are connected), and the refresh-token cold start that the whole cache exists for.
`%LOCALAPPDATA%/Physalia/mcp-auth/` is created but stays EMPTY until consent completes — which is
itself the right behaviour to expect.

**The un-consented failure is legible, which matters more than it sounds.** The node warns
`MCP request 'initialize' to 'notion' did not complete` and then quotes the bridge's own stderr,
including the authorize URL. So a user who missed the browser window, or closed it, can see exactly
what was waiting for them instead of a bare timeout.

**Also confirmed:** `--header` (static bearer) and `--scope` narrowing were NOT exercised; blank is
the normal case and that is what ran.

**Environment trap:** `npx`/`npm` are NOT on the PATH a Windows process inherits when launched from
WSL, so a bare `npx.cmd` fails with "not recognized" — write a `.cmd` that sets
`PATH=C:\Program Files\nodejs;%PATH%` and launch that. Note the asymmetry: `uvx` WAS on the PATH
Rhino itself had, which is why the Blender server resolved from a bare `uvx` while this did not.
