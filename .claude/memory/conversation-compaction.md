---
name: conversation-compaction
description: "Conversation compaction subsystem (sliding/token/anchored window, content prune, LLM summarizer) — Core + new GH Compaction tab"
metadata: 
  node_type: memory
  type: project
  originSessionId: c4ef98d3-9f0b-4831-a5c4-409d6c488096
---

Conversation compaction subsystem landed 2026-06-27 (builds clean; live-Rhino test pending). Fills the Recorder's existing `Conversation` override hook (replaces active convo, Recorded History keeps everything) and realizes the spec's `Distiller`.

**Core** (`Physalia.Core/Compaction/`, pure, GH-free):
- `CompactionInvariants.Reassemble(IEnumerable<ConversationMessage>)` — **keystone**: rebuilds a provider-valid conversation from any cut (drops leading assistant, strips orphan tool_result by tracking tool_use ids, merges consecutive same-role, re-Appends). Every strategy funnels through it.
- `ConversationCompactor` (static, deterministic): `KeepRecentMessages`, `KeepWithinTokenBudget` (uses `ITokenEstimator`, drops oldest until tail fits), `KeepHeadAndTail`, `Prune(PruneOptions)` (drop images/tool-exchanges/feedback, truncate tool-result/text).
- `ConversationSummarizer.SummarizeAsync(...) → Result<CompactionResult,LlmError>` — summary-buffer pattern via `ILlmProvider`; splits [older|recent], summarizes older into one IsFeedback user turn, splices before recent.
- `CompactionResult` / `PruneOptions` records.

**GH** (`src/Physalia.GH/Components/Compaction/`, new **Compaction** ribbon tab): `CompactionComponentBase : RoutingComponentBase<Instructions>` — **components take `Instructions` (system prompt + conversation), NOT a bare Conversation** (per Thomas 2026-06-27): the system prompt is always counted toward a token budget but NEVER compacted; only `instructions.Conversation` is compacted, and only the compacted conversation rides back on the **Success Signal** (Recorder re-attaches its own system prompt). Wire Recorder.**Instructions**→compactor. Routing, NOT plain dataflow. TokenWindow dropped its separate System Prompt input (now from Instructions). TokenThreshold also takes typed Instructions. Components: Sliding Window, Token Window (rejects async `AsyncMarkerTokenEstimator`; use Heuristic/Tiktoken), Anchored Window, Content Pruner; Summarizer is async (`AutoScheduleRead=false`, Reasoner-style; stamps SessionKey=InstanceGuid; the Distiller) — inherits RoutingComponentBase directly, not CompactionComponentBase.

**DAG cycle SOLVED via routing (reworked 2026-06-27 per Thomas):** wiring is Recorder.RecordedHistory→Compactor.Conversation (normal wire) + trigger→Compactor.Signal; Compactor.SuccessSignal→Feedback→(grip-link)→FeedbackCollector→Recorder.Conversation (wireless, breaks the cycle — same path as the feedback correction loop [[ghjson-feedback-links]]). To carry a whole Conversation on a signal: **`PhySignal` gained optional `Conversation? Conversation`** (like the multimodal ContentBlocks extension); `LatchSuccess` + `RoutingResult.Ok` thread it; `FeedbackCollector` preserves it across a batch (latest wins). **Recorder's Conversation input changed from `Param_Conversation` to a `Signal` input** — consumed like other signals, applies override + latches quietly (no re-fire); Recorded History left untouched (compaction = derived view, not a new record). Trigger is signal-driven: manual ConstructSignal OR the **Token Threshold** gate (`TokenThreshold`/`TokGate`, GUID 02342020-637B-43CB-92A0-5A8DA63B025C) — StatefulComponentBase signal source; estimates active-context tokens (wire Recorder.Instructions in), mints a Signal on the rising edge across a Threshold (edge-triggered + first-solve baseline so reload/paste/over-budget doesn't auto-fire; re-arms after compaction drops tokens). Sync estimators only. Full auto loop: Recorder.Instructions→TokenThreshold→Compactor.Signal; Recorder.Instructions→Compactor.Instructions; Compactor.Success→Feedback→FeedbackCollector→Recorder.Conversation. Compacts the ACTIVE context (Instructions), not the full RecordedHistory (which stays untouched ground truth).

Report `planning/conversation-compaction.md` (full research + impl). Deferred: server-side Anthropic compact/clear_tool_uses passthrough, layered pipeline, disk offload, dedup, external memory/RAG. Skipped: importance-scoring eviction.

Related: [[v2-architecture]], signal lifecycle (`planning/data-marshalling.md`).
