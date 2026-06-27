# Conversation Compaction — Research & Implementation

> Status: **landed 2026-06-27** (builds clean). Core logic in `Physalia.Core/Compaction/`;
> GH components in `src/Physalia.GH/Components/Compaction/` (new **Compaction** ribbon tab).
> Live-Rhino test pending.

This document covers (1) research into conversation/context compaction strategies for LLM
agents, and (2) the compaction subsystem implemented for Physalia. The hook already existed:
the **Recorder** carries an optional `Conversation` override input — _"Replaces the active
conversation while all messages are preserved in Recorded History"_ — and the primitives spec
names a `Distiller` (a Reasoner with a summarization instruction). This work fills that hook
with a family of deterministic Core transforms plus one LLM-backed summarizer.

---

# Part 1 — Research

Context-window management for LLM agents has converged on a small set of orthogonal technique
families. They differ along four axes that matter for an implementer: how much information they
preserve, token/latency cost, whether they are deterministic, and whether they require an extra
LLM call. In production they are **composed, not chosen exclusively**: cheap deterministic
pruning reclaims space first, summarization condenses what remains, and external memory
guarantees nothing load-bearing is permanently lost.

**One correctness invariant cuts across every family in agentic settings:** never orphan a
`tool_use` from its `tool_result`, and never drop the system/head message. Providers reject
malformed role/tool sequences with hard 400s, so all serious implementations operate on atomic
message groups and pin the head. (This is exactly what `CompactionInvariants.Reassemble` enforces
— see Part 2.)

## Sliding / recency window (FIFO truncation)

**How.** Keep only the most recent slice; discard older content via FIFO so the prompt never
exceeds the window. Bounded by **message/turn count** (keep last *k*) or by **token budget** (sum
newest→oldest until the next message would exceed `max_tokens`). The tool-aware variant evicts on
**logical turn boundaries** so a `tool_use` is never separated from its `tool_result`; the
production config also **pins the system message** and starts the trim on a valid role.

**Pros.** Dead simple, fully deterministic, zero extra calls. The token-budget variant respects the
real constraint and maximizes retained history without overflow.
**Cons.** Hard recency cliff — anything older is silently gone, including early instructions.
Message-count bounding maps to wildly variable token counts.
**Use.** Token-budget + tool-aware + system-pinned is the cheap, robust first line of defense.

## Anchored head + tail

**How.** Keep the system/task head *and* the most-recent *N* turns; drop or summarize the middle.
Motivated by **"lost in the middle"** (Liu et al. 2023 — retrieval accuracy is U-shaped, strong at
primacy/recency, weak in the middle) and the **attention-sink** effect (StreamingLLM, Xiao et al.
2024 — initial tokens absorb disproportionate attention). The head carries the task definition and
stabilizes attention; the tail carries recency.

**Pros.** Retains both task definition and recent progress in the privileged positions. Pure
truncation is free and deterministic.
**Cons.** Pure truncation loses the middle wholesale and can break causal references.
**Use.** Pure truncation as an emergency backstop; summarized-middle for long sessions where the
middle holds decisions you can't fully lose.

## Summarization (progressive · recursive · hierarchical · summary-buffer)

Replaces older raw turns with model-generated summaries; variants differ in *what* is re-summarized
each step.

- **Progressive / running summary** — one evolving summary string; each turn feeds
  `prior_summary + new_turn → updated summary`. Constant size, cheapest steady state, but
  lossy-*compounding* (errors accumulate).
- **Summary-buffer hybrid (recent verbatim + summarize older)** — keep recent messages verbatim
  plus one rolling summary of everything older, governed by a token budget; only evicted messages
  fold into the summary. Exact recall of recent work + bounded total + long-range gist; summarizer
  runs rarely. **This is the de-facto production standard.**
- **Recursive summarization** (Wang et al. 2023) — memory regenerated holistically each step
  (`M_i = LLM(M_{i-1}, S_i)`); coherent and temporally aware but early errors propagate.
- **Hierarchical** — a tree (raw → chunk summaries → root); scales to arbitrary length with
  multi-resolution recall, at high build/maintenance complexity.

## Selective pruning / filtering

Per-message operations (drop, truncate, dedup, offload) that preserve transcript shape rather than
rewriting it. Universal hazard: breaking tool-pair coherence.

- **Tool-result clearing** — drop oldest tool results (leave a placeholder), keep the *N* most
  recent pairs. The biggest, stalest token sink in agent loops. Deterministic, no LLM call.
- **Thinking-block clearing** — drop prior extended-thinking blocks.
- **Size-cap + disk offload** — cap each tool result; persist full output to a file, leave a
  preview + path. Recoverable, nothing truly lost.
- **Deduplication** — stub redundant idempotent reads. Near-lossless.
- **Oversized-payload head/tail** — keep beginning + end of one large blob, drop the interior.
- **Importance-based eviction** — score messages and evict lowest. *More researched than deployed*
  — production prefers recency + recoverability for determinism/debuggability.
- **Image / modality stripping** — replace older images with a placeholder; keep recent visual turns.

## External memory & retrieval

Treats the window as fast RAM backed by larger external "disk".

- **Tiered virtual-context (MemGPT/Letta)** — OS-style hierarchy (bounded core/working memory +
  FIFO recent queue, backed by recall + archival vector stores); the LLM self-manages paging.
- **Scratchpad / working memory** — a small bounded in-context region the agent rewrites as
  source-of-truth.
- **RAG retrieval of past turns** — embed turns, semantic top-*k* per step; cost grows with *k*,
  not history length, but retrieves "relevant" over "critical".
- **Vector-store extracted memory (Mem0/Zep/LangMem)** — an LLM extracts durable facts and
  add/update/deletes them; high compression, extra LLM pass.

## Real-world usage

- **Anthropic Messages API (server-side).** `compact_20260112` is a summary-buffer hybrid (emits a
  `compaction` block past a token `trigger`, auto-drops earlier blocks, cache-aware,
  `pause_after_compaction`). `clear_tool_uses_20250919` + `clear_thinking_20251015` are
  deterministic context editing (clear oldest tool results / thinking past a trigger, `keep` recent,
  `clear_at_least` to protect cache, `exclude_tools` whitelist).
- **Claude Code / Agent SDK.** `/compact` + auto-compact (~95% of window) are the CLI surface of the
  same summary-buffer idea; layered with client-side caps (~50K chars/tool), disk offload (~2KB
  previews), and file-read dedup. Guidance is explicit: *compaction alone is insufficient* — pair it
  with external memory (requirements/progress files, git).
- **LangChain / LangGraph.** `trim_messages` (token/count window; canonical
  `strategy="last", include_system=True, start_on="human"`); legacy `ConversationBufferWindowMemory`
  / `ConversationSummaryMemory` / `ConversationSummaryBufferMemory`; `RemoveMessage` for persistent
  state reduction; `langmem.SummarizationNode`. Footgun: summarizing without `RemoveMessage` grows
  state unbounded.
- **Microsoft Agent Framework / Semantic Kernel.** Truncation / SlidingWindow / ToolResult /
  Summarization strategies, all preserving system groups + a `MinimumPreserved` floor and computing
  a "safe boundary index" to avoid orphaning tool pairs; composed under `PipelineCompactionStrategy`.
- **MemGPT / Letta.** Reference tiered memory (core + recall + archival; ~70% warn / 100%
  evict-and-recursively-summarize).

## Comparison

| Method | Preserves info | Cost | Determinism | Needs LLM call |
|---|---|---|---|---|
| Message-count window | Low (recency cliff) | Negligible | Full | No |
| Token-budget window | Low–Med | Low (tokenizer) | Full | No |
| Head+tail pure truncation | Low (middle lost) | Negligible | Full | No |
| Head+tail summarized-middle | Medium | Med (1 call) | No | Yes |
| Progressive summary | Low–Med (compounding) | High (every turn) | No | Yes |
| Summary-buffer hybrid | Med–High (recent verbatim) | Med (on eviction) | No | Yes |
| Recursive summarization | Medium (coherent) | High | No | Yes |
| Hierarchical summarization | High (multi-res) | High | No | Yes |
| Tool-result clearing | Med (placeholder) | Negligible | Full | No |
| Size-cap + disk offload | High (recoverable) | Low (I/O) | Full | No |
| Deduplication | High (near-lossless) | Negligible | Full | No |
| Importance scoring eviction | Med–High | Med–High | Low | Often |
| Image / modality stripping | Med | Negligible | Full | No |
| Tiered memory (MemGPT) | High (nothing lost) | High | No | Yes |
| RAG retrieval of turns | High (if retrieved) | Med | No | Embed only |

## Recommendations for Physalia (from the research)

`Physalia.Core` is a pure functional library, so **deterministic compaction belongs in Core** and
anything requiring a generation pass belongs at the provider layer.

- **Tier 1 — deterministic Core functions (do first):** token-budget tool-aware system-pinned
  window; tool-result clearing / size-cap; dedup; image stripping; head+tail backstop. These
  compose into a layered pipeline gated by one token budget.
- **Tier 2 — needs an LLM call:** summary-buffer hybrid (policy in Core, generation through a
  provider); optionally pass through Anthropic's server-side `compact`/`clear_tool_uses`.
- **Tier 3 — later:** external memory + RAG (note: Physalia's lifecycle is currently session-only,
  nothing serializes — external memory is a deliberate architectural change, not a drop-in).
  Importance-scoring eviction: skip.

Order of preference: `dedup → tool-result clearing → image stripping → token-budget window →
(server-side compaction | summary-buffer) → head+tail backstop`.

---

# Part 2 — Implementation

The implementation follows the Tier-1/Tier-2 split. All transforms produce a provider-valid
`Conversation`; the lossy/LLM step is isolated in one async component.

## Core — `Physalia.Core/Compaction/`

Pure, GH-free, side-effect-free (the boundary rule holds — only the summarizer touches a provider,
through the existing `ILlmProvider` abstraction Core already owns).

| File | What |
|---|---|
| `CompactionResult.cs` | Record: compacted `Conversation` + original/retained message counts (`DroppedMessageCount`). `From(original, compacted)` and `Unchanged(c)` factories. |
| `CompactionInvariants.cs` | **The keystone.** `Reassemble(IEnumerable<ConversationMessage>)` rebuilds a provider-valid conversation from an arbitrarily-cut sequence. |
| `PruneOptions.cs` | Record selecting what `Prune` drops/truncates (images, tool exchanges, feedback turns, max tool-result chars, max text chars). All default off. |
| `ConversationCompactor.cs` | The four **deterministic** strategies (static, pure). |
| `ConversationSummarizer.cs` | The **LLM-backed** summary-buffer strategy (async, via `ILlmProvider`). |

### `CompactionInvariants.Reassemble` — why it exists

Every cut (window, head+tail, prune) can produce an invalid conversation: a leading assistant turn,
an orphaned `tool_result` whose `tool_use` was dropped, or two consecutive same-role turns where the
dropped span sat between them. Providers reject all three. `Reassemble` is the single funnel every
strategy passes through. It:

1. Drops leading assistant turns (a conversation must open with a user turn).
2. Strips orphan `tool_result` blocks — tracking `tool_use` ids in causal order, so a result whose
   request was compacted away is removed; a message left empty is dropped.
3. Re-trims any leading assistant exposed by step 2.
4. Merges consecutive same-role messages (content blocks concatenate; `IsFeedback` survives only
   when both were feedback).
5. Rebuilds via `Conversation.Append`, which re-validates alternation (now guaranteed).

This is exactly the "operate on atomic groups, pin the head, never orphan tool pairs" rule the
research flags as the cross-cutting correctness invariant.

### `ConversationCompactor` (deterministic)

- `KeepRecentMessages(conversation, maxMessages)` — sliding recency window by message count.
- `KeepWithinTokenBudget(conversation, systemPrompt, ITokenEstimator, maxTokens)` — drops oldest
  messages one at a time, re-estimating the reassembled tail (incl. system prompt) until it fits.
  A single message larger than the budget is kept alone (deterministic compaction can't shrink one
  message — that's the summarizer's job). Reuses the existing `ITokenEstimator`.
- `KeepHeadAndTail(conversation, headCount, tailCount)` — anchored window; keeps the first K and
  last M, drops the middle. If head and tail abut at the same role, `Reassemble` merges them.
- `Prune(conversation, PruneOptions)` — content-aware filter: removes images / tool exchanges /
  feedback turns and truncates over-long tool results or text (with a `… [truncated N characters]`
  marker), then reassembles. Covers the research's "tool-result clearing", "size-cap", and
  "image stripping" wins in one component.

### `ConversationSummarizer` (LLM-backed, summary-buffer)

`SummarizeAsync(conversation, ILlmProvider, ModelConfig, instruction, keepRecentMessages, ct)
→ Result<CompactionResult, LlmError>`. Implements the production-standard summary-buffer pattern:

1. Split into `[older | recent]` at `count - keepRecentMessages`.
2. Render the older portion (reassembled) as a single user turn and run one forward pass with a
   compaction system prompt (`DefaultInstruction`, overridable).
3. Splice: a single summary turn (`[Summary of earlier conversation] …`, marked `IsFeedback` so the
   UI styles it as machine-generated) followed by the recent turns verbatim → `Reassemble`.

Nothing old enough to summarize ⇒ returned unchanged. The orchestration is in Core; only the
provider call crosses the boundary, mirroring how `Reasoner` already streams.

## GH — `src/Physalia.GH/Components/Compaction/` (new "Compaction" ribbon tab)

The compaction components are **routing components** (`RoutingComponentBase<Instructions>`), not plain
dataflow transforms. This is the fix for the DAG cycle (see Part 3): a direct wire from a Recorder's
output back to its own `Conversation` input would be an illegal cycle, so the compacted result must
travel as a **signal** that can be carried wirelessly across the DAG by a Feedback Collector.

They take **`Instructions`** (system prompt + conversation), not a bare conversation. The system prompt
is **always included** when measuring a token budget but is **never compacted** — only the conversation
is — and the compacted conversation alone rides back on the signal (the Recorder re-attaches its own
system prompt). Wire a Recorder's **Instructions** output into the input.

`CompactionComponentBase : RoutingComponentBase<Instructions>` owns the shared contract for the
deterministic four: a typed `Instructions` input (the source, index 0) + the base-owned `Signal`
trigger (appended last); `TryGetData` reads the source instructions from the typed input (the trigger
signal just says "go", exactly like the Reasoner ignoring its signal payload and reading Instructions);
`PushSolve` is empty (synchronous); `ReadSolve` calls the subclass's `Compact(Instructions)` — which
compacts `instructions.Conversation` — and returns a success result **carrying the compacted
conversation on the minted Success Signal**. Outputs are the standard `Success Signal` (0) /
`Fail Signal` (1).

| Component | Nick | GUID | Strategy |
|---|---|---|---|
| Sliding Window | `Window` | `D731E821-…` | `KeepRecentMessages` (Max Messages) |
| Token Window | `TokWin` | `82B8ED80-…` | `KeepWithinTokenBudget` (estimator + Max Tokens; system prompt from Instructions, counted but never dropped) |
| Anchored Window | `Anchor` | `ABA68278-…` | `KeepHeadAndTail` (Keep First / Keep Last) |
| Content Pruner | `Prune` | `EE741363-…` | `Prune` (Drop Images/Tools/Feedback, Max Tool-Result/Text Chars) |
| Summarizer | `Distill` | `8241DBD1-…` | `SummarizeAsync` (Model + Summary Prompt + Keep Recent) |
| Token Threshold | `TokGate` | `02342020-…` | auto-trigger gate (Instructions + estimator + Threshold → Signal) |

- **Token Window** rejects the async API-backed estimators (`AsyncMarkerTokenEstimator` — Anthropic
  / Gemini / LlamaCpp throw on the synchronous `Estimate`) with a clear warning; use **Heuristic** or
  **Tiktoken**.
- **Summarizer** is the one async component. It mirrors the Reasoner's async routing pattern
  (`AutoScheduleRead => false`; the read pass fires from the inference completion callback) rather than
  inheriting `CompactionComponentBase`. It stamps `config.SessionKey = InstanceGuid` so a warm Claude
  Code session is per-component, and cancels its task on `RemovedFromDocument`. This is Physalia's
  concrete **Distiller**.

### Signal-plumbing changes that make this work

Carrying a whole `Conversation` back to the Recorder required a small, contained extension to the
signal system (analogous to the existing multimodal `ContentBlocks` extension):

- **`PhySignal`** gains an optional `Conversation? Conversation` carrier (a conversation is a sequence
  of role-tagged turns that neither the string `Payload` nor the flat `ContentBlocks` can represent).
  `Mint` gains a matching optional parameter.
- **`StatefulComponentBase.LatchSuccess`** and **`RoutingComponentBase.RoutingResult.Ok`** gain an
  optional `conversation` that flows onto the minted Success Signal.
- **`FeedbackCollector`** preserves the conversation across an injected batch (latest wins), alongside
  the payload-join and content-block concatenation it already did.
- **`Recorder`**: its `Conversation` input changed from a typed `Param_Conversation` to a **`Signal`
  input** (so a Feedback Collector can feed it). It is observed and consumed like the other signal
  inputs; a consumed signal carrying a `Conversation` replaces the active conversation and latches
  quietly (no outgoing signal, so it never fires a new run). **Recorded History is left untouched** —
  a compaction is a derived view of the full ground-truth log, not a new record.

---

# Part 3 — Integration & wiring

## The compaction loop (cycle resolved)

The dataflow is: **Recorder `Instructions` → Compactor `Instructions`** (a normal DAG wire) and a
**trigger `Signal` → Compactor `Signal`**; the compactor compacts the conversation (the system prompt
is preserved untouched) and emits the compacted conversation on its **Success Signal**, which routes
**Compactor → Feedback →(grip-link)→ Feedback Collector → Recorder `Conversation`**. The grip-link from
Feedback to Feedback Collector is wireless — it is *not* a GH wire — so the loop back into the Recorder
never forms an illegal cycle. This is the same mechanism the existing `Feedback → FeedbackCollector →
Recorder` correction loop already uses; compaction simply rides it with the conversation on the signal.

The compactor operates on the **active context** (the Recorder's Instructions — what is actually sent to
the model), not the full log: the Recorder's `Recorded History` remains the untouched ground truth.
Applying the override latches the Recorder quietly (no user signal → the Reasoner does not re-fire). The
compactor runs **once per trigger**.

## Triggering

Because the compactor is signal-driven, it fires only when a `Signal` arrives. Two trigger sources:

- **Manual** — a `Construct Signal` (Button) for on-demand compaction.
- **Automatic** — the **Token Threshold** gate (`TokenThreshold`, `TokGate`). It estimates the token
  count of the active context (wire a Recorder's **Instructions** into its Instructions input) with a
  synchronous estimator and mints a Signal **once each time the count crosses the threshold upward**.
  Wire its Signal into a compactor's Signal input to compact automatically at, say, ~80% of the
  model's context window. The fire is edge-triggered (a context sitting over the threshold does not
  re-fire every solve; the compaction it triggers brings the count back down and re-arms it), and the
  first solve only baselines — a freshly loaded over-budget component does not auto-fire. It also
  exposes a `Token Count` readout. Async API-backed estimators are rejected (use Heuristic/Tiktoken),
  matching the Token Window.

A full auto-compaction loop:
`Recorder.Instructions → Token Threshold → [Signal] → Compactor.Signal`, with
`Recorder.Instructions → Compactor.Instructions` and
`Compactor.Success → Feedback ┄┄▶ Feedback Collector → Recorder.Conversation`.

## Deferred (per the research's Tier 2/3)

- **Server-side compaction passthrough** — surface Anthropic's `compact_20260112` /
  `clear_tool_uses` as provider options on the Anthropic path (zero summarization code, cache-aware);
  the Core pipeline stays the cross-provider fallback.
- **Layered pipeline** — chain `Content Pruner → Token Window → Anchored Window` (dedup/clear/strip →
  window → backstop) as one gated path, mirroring Microsoft's `PipelineCompactionStrategy`.
- **Disk offload** for large tool results (Core emits placeholder + pointer; GH writes the file).
- **Dedup of idempotent tool reads** — a `Prune` extension keyed on a content hash.
- **External memory / RAG** — only when multi-session persistence becomes a requirement (Physalia's
  lifecycle is currently session-only; nothing serializes).
- **Importance-scoring eviction** — intentionally skipped (more researched than deployed; loses the
  determinism a pure Core library wants).

## Sources

LangChain `trim_messages` / buffer-window / summary-buffer memory; Anthropic compaction &
context-editing docs (`platform.claude.com/docs/.../compaction`, `.../context-editing`,
memory-tool); Anthropic "effective context engineering" & "effective harnesses for long-running
agents"; Microsoft Agent Framework / Semantic Kernel context-management blogs &
`learn.microsoft.com/.../compaction`; MemGPT/Letta (arxiv 2310.08560); "Lost in the Middle"
(arxiv 2307.03172); StreamingLLM attention sinks (arxiv 2309.17453); recursive summarization
(arxiv 2308.15022); Mem0 / Zep / LangMem long-term-memory writeups; Google ADK & AWS Bedrock
compaction docs. Full URL list in the research workflow transcript.
