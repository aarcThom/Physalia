---
name: claudecode-warm-process
description: Claude Code provider reworked from per-call cold-start CLI to a warm persistent process pool
metadata: 
  node_type: memory
  type: project
  originSessionId: d9c06c92-5565-4ad3-8f01-c7266304ffa8
---

ClaudeCodeProvider was VERY SLOW because every inference cold-started a fresh `claude -p` subprocess (Node harness + settings + CLAUDE.md + hooks + every global MCP server). Reworked 2026-06-18 to a **warm persistent process** pool. **Why:** the "SDK" is a dead end — no .NET Agent SDK; the Python/TS SDKs just spawn the same `claude` binary and require ANTHROPIC_API_KEY (forbid subscription/OAuth auth), so they can't replace this provider's `claude auth login` value. Old 0.1.0-alpha code wasn't faster — same cold `claude -p` per call; the CLI itself got heavier.

**How it works now:**
- `ClaudeCodeSession` (Core, `Providers/ClaudeCode/`) holds ONE long-lived `claude` process in streaming-input mode: `--input-format stream-json --output-format stream-json --include-partial-messages --verbose --system-prompt-file <tmp> --model <id> --strict-mcp-config --disallowed-tools "..." -p`. `--strict-mcp-config` zeroes MCP servers (biggest startup win); tools denied so it never goes agentic.
- Per turn: write one NDJSON `{"type":"user","message":{"role":"user","content":...}}` line to stdin; read stdout until `{"type":"result",...}`. Text deltas = `stream_event` → `event.type==content_block_delta` → `delta.type==text_delta` (skip thinking/signature). The per-turn `system/init` line is NOT a process-start marker (it appears every turn) — the turn delimiter is the `result` line. Context is retained across turns IN the process (verified: turn 2 recalled turn 1's value; cache_read reused turn 1's cached system prompt).
- `ClaudeCodeProvider` = static `ConcurrentDictionary<Guid, ClaudeCodeSession>` pool keyed on `ModelConfig.SessionKey` (new optional base prop; LLM Call stamps `config with { SessionKey = InstanceGuid }` in PushSolve). First call SEEDS full history as one user message; later calls send only the new user turn (DELTA). **Counting gotcha:** the CLI generates the assistant turn internally, so after a turn the session accounts for `conversation.Count + 1` messages (the +1 = the assistant turn Conversation Log appends after the call). Delta condition: `conversation.Count == ConsumedMessageCount + 1 && last.Role==User`; else reseed (kill+restart, since a running process can't be rewound). Model/system-prompt change or shrink ⇒ reseed.
- Teardown: `LLM Call.RemovedFromDocument` → `ClaudeCodeProvider.EndSession(InstanceGuid)`; idle reaper (15 min) + `ProcessExit` kill-all so no `node`/`claude` leaks. Cancel mid-turn ⇒ session killed + dropped (desynced), next call cold-reseeds.
- Null SessionKey ⇒ ephemeral one-shot session (old cold behaviour). Tools param still ignored (CLI can't surface Physalia tool calls back).

Files: `Providers/ClaudeCode/ClaudeCodeSession.cs` (new), `Providers/ClaudeCode/ClaudeCodeProvider.cs` (rewritten), `Models/ModelConfig.cs` (+SessionKey), `Components/Core/LlmCall.cs`. Note: net7.0 — `StreamWriter.FlushAsync(ct)` is .NET 8+, use `FlushAsync()`. Builds clean. Still UNVERIFIED in live Rhino (warm-vs-cold timing, teardown leak check) — see plan `radiant-dreaming-micali.md`. Relates to [[v2-core-architecture]] and the signal lifecycle [[routing-trigger-system]].
