---
name: tool-calling-gh-loop
description: GH tool-calling loop (LLM Call/Router/tool nodes) and the multi-call tool-node contract
metadata: 
  node_type: memory
  type: project
  originSessionId: efe44c97-79d9-42d6-9249-0aad3c5a6e2f
---

The GH visible tool-calling loop: LLM Call routes a tool-call response on its **aux** `Tool Calls` output (assistant turn = optional TextContent + one ToolCallContent per call), not Success. Router has one variable output per tool (auto-named from the wired tool node's Tool Definition) and a fixed Feedback output. Router → tool node `Signal`; tool node `Result` → Feedback → FeedbackCollector → Router `Results`; Router `Feedback` → Feedback → FeedbackCollector → Conversation Log `Tool Signal`.

**Provider contract:** an assistant turn with N `tool_use` blocks must be followed by exactly ONE user turn carrying a `tool_result` for every id, before any further assistant turn. The Router enforces this by identity: it holds all dispatched ids in `_pendingToolUseIds`, accumulates returning `ToolResultContent` (matched by `tool_use_id`), and forwards ONE combined Feedback signal only when the whole set is satisfied (`ResultsReady()`). Undispatchable calls get a synthetic `is_error` result so the round still completes.

**Tool nodes inherit `ToolComponentBase`** (`Components/Tools/ToolComponentBase.cs`) — it owns the whole contract: advertises `Definition` on the Tool output, observes/consumes the Signal input, and handles the multi-call case. The dispatched signal may carry MORE THAN ONE `ToolCallContent` (the model can call the same tool several times in one turn — parallel tool use), so the base runs `ExecuteCall` once per call and emits ONE result signal whose `ContentBlocks` hold a `ToolResultContent` per call, each echoing that call's `Id`. Answering only the first call strands the other ids as permanently pending → round never completes → Router never forwards the body. Subclass supplies only: `Definition`, optional `RegisterAdditionalInputs` + `OnSolveTick` (cache per-solve context like a catalog), and `ExecuteCall(call) → ToolCallResult` (`Ok`/`Error`). The result body rides in `ToolResultContent.Content`; the Router collects from blocks, not payload, so payload-only results are invisible — the base wraps correctly, don't bypass it. `ComponentSearch` is the reference implementation.

**Why this exists:** parallel calls to the *same* tool collided in the Router's `_dispatched` dict (keyed by output nickname) — the second overwrote the first, but both ids were pending, so the round hung and the body never returned. Fixed (2026-06-17) by grouping calls per output into one dispatched signal + tool node returning one result-per-call. Authoritative doc: `planning/data-marshalling.md` → "Tool calling" section. Relates to [[tool-calling-phase4]].
