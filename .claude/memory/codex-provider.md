---
name: codex-provider
description: "The OpenAI Codex CLI provider — JSON-RPC app-server, warm thread per LLM Call, what was measured rather than guessed (2026-08-16)"
metadata: 
  node_type: memory
  type: project
  originSessionId: 4b03585f-0a85-4efa-88ff-01df7c93a74e
  modified: 2026-08-17T06:23:29.877Z
---

Built 2026-08-16, modelled on [[claudecode-warm-process]]: inference through the locally-installed
`codex` CLI using the user's own `codex login` session, no API key stored or sent.

It can also **call Physalia's LLM Tools** — added the same day, detail in [[codex-dynamic-tools]].
That makes it the only local-CLI provider that can, and it is why the "a CLI cannot surface tool
calls back" assumption recorded against Claude Code is wrong as a general claim.

**Files:** `Physalia.Core/Providers/Codex/{CodexProvider,CodexSession}.cs`,
`Physalia.Core/Models/Named/CodexConfig.cs`, `Physalia.GH/Components/Models/CodexModel.cs`.
Wired at exactly the same five points Claude Code is: `LlmProviderFactory`,
`LlmCall.RemovedFromDocument` (EndSession), `ProviderAvailability` (setup id `codex`),
`Physalia.UI/src/lib/chat/providers.ts`.

**Transport — `codex app-server --stdio`, JSON-RPC 2.0, one line per message.** NOT `codex exec`
(that is one process per turn). Handshake: `initialize` → `initialized` notification →
`thread/start` (once, opens the thread) → `turn/start` per turn. Stream:
`item/agentMessage/delta`, `item/reasoning/summaryTextDelta`, `thread/tokenUsage/updated`, ending
at `turn/completed`. The protocol is self-describing — regenerate it any time with
`codex app-server generate-json-schema --out <dir>` (writes `v2/*.json`, one file per message).

**Measured on CLI 0.142.3, not guessed:**
- `summary: "auto"` on `turn/start` is what makes reasoning visible — without it the server sends a
  `reasoning` item with NO text at all. The exact analogue of Claude Code's `--thinking-display
  summarized` trap ([[claudecode-provider-perf]]).
- `baseInstructions` on `thread/start` genuinely replaces the agent's base prompt (verified with a
  186k-char prompt whose Rule 1337 the model quoted back verbatim).
- A **server-initiated JSON-RPC request must be answered** (`-32601`) or the turn stalls forever.
- `--disable plugins/apps/browser_use/...` + `-c tools.web_search=false` cut ~2.4k input tokens per
  turn and one MCP subprocess. `-c notify=[]` matters: a user's notify hook would otherwise spawn a
  process every turn.
- **A user's own `[mcp_servers]` entry cannot be dropped from the command line** — `-c mcp_servers={}`
  and the thread-level `config` object both MERGE, and the one clean lever (`CODEX_HOME`) is also
  where `codex login` keeps the credentials, so pointing it elsewhere loses the auth. Accepted.
- Model ids go stale fast (`gpt-5.1-codex` is rejected outright on a ChatGPT account), so the model
  list is fetched LIVE via `model/list` and `CodexConfig.KnownModels` is only a seed/fallback. Empty
  `ModelId` = the CLI's own default, which is the choice that never rots.
- **The offered model set is keyed on the INSTALLED CLI VERSION, not just the plan** (the cache
  records `client_version`). On 0.142.3 `model/list` returns gpt-5.5 / gpt-5.4 / gpt-5.4-mini, and
  asking for a newer generation fails 400: "The 'gpt-5.6-sol' model requires a newer version of
  Codex." Not a client-name or capability gate — `codex_cli_rs`, `codex_vscode`, `experimentalApi`
  and `includeHidden` all return the same set. So a newer Codex's picker showing more models than
  Physalia does means the `codex` on PATH is older; upgrading the CLI is the whole fix, and the live
  fetch picks the new models up with no rebuild.
- The agent classifies an upstream failure as `codexErrorInfo: "other"` and dumps the provider's raw
  JSON body into `message`; `DescribeAgentError` feeds that to `HttpErrorMapper` instead, turning it
  into `InvalidRequest` + "HTTP 400 BadRequest: invalid_request_error — …".

**Verified end-to-end against the live CLI** through a throwaway console harness referencing
Physalia.Core — see [[core-console-harness]] for the technique (seed 3.1s → warm delta 1.5s, inline
`<think>`, cache-read usage, live model list, bad-model error, 186k-char system prompt, mid-turn
cancellation, no orphaned processes).
**NOT yet run inside Rhino** — the build's copy into `bin` was blocked by a running Rhino 8
([[physalia-repo-gotchas]] has the filter for reading past that lock error).

**Known duplication:** `CodexProvider` is ~120 lines that mirror `ClaudeCodeProvider` almost exactly
(pool, reaper, seed-vs-delta, error wrapping). A shared `CliSessionProviderBase` is the obvious
follow-up; deliberately not done here, since it would touch the already-working Claude Code path.
