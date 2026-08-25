---
name: chat-token-counter
description: "Chat window token counter (bottom-right) — SUPERSEDED 2026-08-24: it is now driven by the grip-linked Token Count human tool, not a downstream walk"
metadata: 
  node_type: memory
  type: project
  originSessionId: a585e471-72d4-4dbc-91c6-8dd9058ed1c6
---

**SUPERSEDED 2026-08-24 — see [[token-count-human-tool]].** The downstream walk described below (`PromptPipelineView.GetDownstreamTokenCount`) is DELETED; the counter is now driven by a Token Count human tool grip-linked to a specific Token Estimator, read via `ConversationLog.LinkedTokenCountOrNull`. The bridge verb `setTokenCount` and the Svelte counter element are unchanged. Kept for the original wiring notes.

**Chat token counter** (2026-07-03, branch feat/memory-tool): the chat window shows the estimated token count at its bottom-right corner, but ONLY when a Token Estimator is wired downstream of the active Chat's Conversation Log (per-chat specific — re-resolved when the switcher changes chats). No estimator → nothing rendered.

Implementation:
- `PromptPipelineView.GetDownstreamTokenCount(Conversation Log)` (`src/Physalia.GH/Components/Core/PromptPipelineView.cs`) — reuses the private `DownstreamSignalComponents` spine walk (Conversation Log Signal output → gates/compactors, stops at LLM Call) to find the first `TokenEstimator`, then reads `Params.Output[0].VolatileData` first `GH_Integer` (no public property on TokenEstimator; VolatileData always matches the canvas). Null when unwired or no count yet. Note: TokenEstimator's Data input consumes the signal wire directly (`TokenInputHelper.TryResolve` unwraps `GH_Signal.Instructions`), so spine-recipient search matches the sanctioned wiring.
- `ChatWindow.Tick()` pushes new bridge verb `setTokenCount(int|null)` with `_lastTokenCount` change-detection + `_forcePush` (Chat switch), same pattern as `setStream`.
- Svelte: `PhysaliaHost.setTokenCount` in `bridge.ts`; `tokenCount` $state + handler in `App.svelte`; counter element absolutely positioned `right-3 bottom-2` in `<main>` (now `relative`), muted 11px `toLocaleString()` text, hidden during setup.

Builds clean (UI embedded via dotnet build), 191 tests green, svelte-check clean. **Live Rhino test pending.**
