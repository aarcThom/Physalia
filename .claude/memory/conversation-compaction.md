---
name: conversation-compaction
description: "Conversation compaction subsystem (sliding/token/anchored window, content prune, LLM summarizer) — Core + new GH Compaction tab"
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Conversation compaction subsystem landed 2026-06-27 (builds clean; live-Rhino test pending). Inline forward-path compaction on `Conversation Log → Compactor → LLM Call` (see ARCHITECTURE note below); realizes the spec's `Distiller`.

**Core** (`Physalia.Core/Compaction/`, pure, GH-free):
- `CompactionInvariants.Reassemble(IEnumerable<ConversationMessage>)` — **keystone**: rebuilds a provider-valid conversation from any cut (drops leading assistant, strips orphan tool_result by tracking tool_use ids, merges consecutive same-role, re-Appends). Every strategy funnels through it.
- `ConversationCompactor` (static, deterministic): `KeepRecentMessages`, `KeepWithinTokenBudget` (uses `ITokenEstimator`, drops oldest until tail fits), `KeepHeadAndTail`, `Prune(PruneOptions)` (drop images/tool-exchanges/feedback, truncate tool-result/text).
- `ConversationSummarizer.SummarizeAsync(...) → Result<CompactionResult,LlmError>` — summary-buffer pattern via `ILlmProvider`; splits [older|recent], summarizes older into one IsFeedback user turn, splices before recent.
- `CompactionResult` / `PruneOptions` records.

**ARCHITECTURE REWORKED 2026-06-27 (per Thomas) — Instructions ride the signal; inline forward-path compaction.** The Conversation Log now **outputs a Signal only**, carrying the full `Instructions` (system prompt + conversation); the typed Instructions output AND Recorded History output were removed; the Conversation override input + loop-back machinery were removed. The trigger IS the data. See [[signal-carrier-discipline]].

Flow: `Prompter → Conversation Log → [Compactor] → LLM Call`; response back via `LLM Call → Feedback → FeedbackCollector → Conversation Log` (wireless, the only loop — so adding a compactor on the forward path stays acyclic). The Conversation Log is the **uncompacted source of truth** (`ActiveConversation` always full); a compactor only transforms the copy on the signal. The LLM Call reads `signal.Instructions` (its typed Instructions input was removed).

**GH** (`src/Physalia.GH/Components/Compaction/`, **Compaction** tab): `CompactionComponentBase : RoutingComponentBase<Instructions>` — `TryGetData` reads `signal.Instructions` (no typed input; subclass param indices start at 0); compacts `instructions.Conversation` (system prompt preserved, counted in token budget but never compacted); re-emits a Signal carrying `new Instructions(source.SystemPrompt, compactedConversation)` on **Success**, wired straight to the LLM Call. Components: Sliding Window, Token Window (sync estimators only — rejects `AsyncMarkerTokenEstimator`), Anchored Window, Content Pruner; **Summarizer** is async (`AutoScheduleRead=false`, LLM Call-style; SessionKey=InstanceGuid; the Distiller), standalone RoutingComponentBase<Instructions>. **Token Threshold** gate (`TokGate`, GUID 02342020-637B-43CB-92A0-5A8DA63B025C) is a **router** (StatefulComponentBase, SignalLimiter-style): consumes the Conversation Log's Signal, estimates the carried Instructions' tokens, re-emits the SAME signal (Instructions intact) on **Under Limit** (≤ threshold) or **Over Limit** (> threshold). Gated auto loop: `Conversation Log.Signal → TokGate`; `Under → LLM Call`; `Over → Compactor → LLM Call` — every turn reaches the LLM Call once (consume-once dedupes), compaction only on over-budget turns. (NOT the old mint-on-crossing edge-trigger — that minted a bare signal with no Instructions and skipped under-budget turns; broken in the inline model.)

**Signal carrier:** `PhySignal` carries an optional `Instructions` (replaced the short-lived `Conversation` carrier). `LatchSuccess`/`RoutingResult.Ok` thread `instructions:`. `GH_Signal` casts to Instructions/Conversation/text; `DeconstructSignal` surfaces an Instructions output; `TokenInputHelper` resolves a signal's Instructions. **Do not add more carrier fields** ([[signal-carrier-discipline]]).

Report `planning/conversation-compaction.md` (full research + impl). Deferred: server-side Anthropic compact/clear_tool_uses passthrough, layered pipeline, disk offload, dedup, external memory/RAG. Skipped: importance-scoring eviction.

Related: [[v2-architecture]], signal lifecycle (`planning/data-marshalling.md`).
