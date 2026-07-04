# Conversation Compaction — Research & Implementation

> Status: **landed 2026-06-27** (builds clean). Core logic in `Physalia.Core/Compaction/`;
> GH components in `src/Physalia.GH/Components/Compaction/` (new **Compaction** ribbon tab).
> Live-Rhino test pending.

This document covers (1) research into conversation/context compaction strategies for LLM
agents, and (2) the compaction subsystem implemented for Physalia: a family of deterministic Core
transforms plus one LLM-backed summarizer (the spec's `Distiller`).

> **Note (architecture reworked 2026-06-27):** compaction is now an **inline forward-path**
> transform — the Conversation Log emits a Signal carrying the full Instructions, a compactor sits on
> `Conversation Log → Compactor → LLM Call` and re-emits compacted Instructions, and the Conversation Log stays the
> uncompacted source of truth. The earlier design (a `Conversation` override input + a wireless
> loop-back) was removed. Part 2/3 below describe the current model; some Part 1 research framing still
> references "feed the compacted view back to the Conversation Log" — read it as the forward-path model.

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
provider call crosses the boundary, mirroring how `LLM Call` already streams.

## GH — `src/Physalia.GH/Components/Compaction/` (new "Compaction" ribbon tab)

> **Architecture reworked 2026-06-27 (per Thomas): Instructions ride the signal; compaction is an
> inline forward-path transform.** The Conversation Log emits a **Signal carrying the full `Instructions`**;
> a compactor sits inline on `Conversation Log → Compactor → LLM Call`, consuming that signal and re-emitting
> one carrying the compacted Instructions. No loop-back, no Conversation override, no
> wireless-conversation machinery. The Conversation Log is the uncompacted source of truth (its
> `ActiveConversation` is always the full log); the compactor only transforms the copy on the signal.
> The earlier loop-back design (compactor → Feedback Collector → Conversation Log override) is gone.

The compaction components are **routing components** (`RoutingComponentBase<Instructions>`). The signal
carries the `Instructions` (system prompt + conversation): the system prompt is **always included** when
measuring a token budget but is **never compacted** — only the conversation is — and the re-emitted
signal carries `new Instructions(originalSystemPrompt, compactedConversation)`.

`CompactionComponentBase : RoutingComponentBase<Instructions>` owns the shared contract for the
deterministic four: the subclass's own params (index 0+) + the base-owned `Signal` trigger (appended
last); `TryGetData` reads `signal.Instructions` (the trigger *is* the data — no typed input);
`PushSolve` is empty (synchronous); `ReadSolve` calls the subclass's `Compact(Instructions)` — which
compacts `instructions.Conversation` — and returns a success result **carrying the compacted Instructions
on the minted Success Signal**, wired straight to the LLM Call. Outputs are the standard
`Success Signal` (0) / `Fail Signal` (1).

| Component | Nick | GUID | Strategy |
|---|---|---|---|
| Sliding Window | `Window` | `D731E821-…` | `KeepRecentMessages` (Max Messages) |
| Token Window | `TokWin` | `82B8ED80-…` | `KeepWithinTokenBudget` (estimator + Max Tokens; system prompt counted but never dropped) |
| Anchored Window | `Anchor` | `ABA68278-…` | `KeepHeadAndTail` (Keep First / Keep Last) |
| Content Pruner | `Prune` | `EE741363-…` | `Prune` (Drop Images/Tools/Feedback, Max Tool-Result/Text Chars) |
| Summarizer | `Distill` | `8241DBD1-…` | `SummarizeAsync` (Model + Summary Prompt + Keep Recent) |
| Token Threshold | `TokGate` | `02342020-…` | router gate: Signal in → **Under Limit** / **Over Limit** by token budget (re-emits the same Instructions-signal) |

- **Token Window** rejects the async API-backed estimators (`AsyncMarkerTokenEstimator`); use
  **Heuristic** or **Tiktoken**.
- **Summarizer** is the one async component (`AutoScheduleRead => false`; the read pass fires from the
  inference completion callback). It stamps `config.SessionKey = InstanceGuid` and cancels on
  `RemovedFromDocument`. Standalone `RoutingComponentBase<Instructions>` (not `CompactionComponentBase`).
  Physalia's concrete **Distiller**.
- **Token Threshold** keeps a typed `Instructions` input; a Conversation Log's signal feeds it via the
  `Signal → Instructions` cast (it measures, it doesn't consume).

### Signal-plumbing changes that make this work

The signal is the single inter-component wire and now carries the inference context (the carrier
discipline: `Payload` + `ContentBlocks` + `Instructions`, and nothing more):

- **`PhySignal`** carries an optional `Instructions` (this replaced the short-lived `Conversation`
  carrier). `Mint`, `StatefulComponentBase.LatchSuccess`, and `RoutingComponentBase.RoutingResult.Ok`
  thread an `instructions:` arg onto the minted Success Signal.
- **`Conversation Log`** outputs a **Signal only**, minted on a user/feedback/tool-result turn, carrying
  `new Instructions(systemPrompt, fullConversation)`. Its typed Instructions output and Recorded
  History output were removed; the Conversation override input and `ApplyConversationOverride` were
  removed; `FeedbackCollector`'s conversation preservation was reverted.
- **`LLM Call`** dropped its typed Instructions input and reads `signal.Instructions`.
- **`GH_Signal.CastTo`** casts a signal to `Instructions`/`Conversation`/text; **`DeconstructSignal`**
  surfaces an Instructions output; **`TokenInputHelper`** resolves a signal's Instructions — so a
  signal wire drops into any typed Instructions/Conversation input without manual deconstruction.

---

# Part 3 — Integration & wiring

## The compaction path (no cycle, no loop-back)

```
Prompter ─(Prompt Signal)→ Conversation Log ─(Signal w/ full Instructions)→ [Compactor] → LLM Call
                              ▲                                                       │
                              └──── Feedback ┄┄▶ FeedbackCollector ←─────────────────┘  (response, wireless)
```
The forward path `Conversation Log → Compactor → LLM Call` is plain wires; the only loop back to the Conversation Log is
the existing **wireless** response link (`LLM Call → Feedback →(grip-link)→ FeedbackCollector →
Conversation Log.Response`), so nothing forms an illegal cycle. The compactor consumes the Conversation Log's signal,
compacts the carried Instructions, and re-emits a signal carrying the compacted Instructions to the
LLM Call. The Conversation Log keeps the full uncompacted log (the LLM Call's response appends to it); compaction
only ever transforms the copy on the signal. Without a compactor, `Conversation Log → LLM Call` directly.

## Triggering

A compactor placed directly on the path (`Conversation Log → Compactor → LLM Call`) runs **every turn**. To
compact only when the context is actually large, gate it with the **Token Threshold** (`TokGate`) — a
**router**, not a trigger-minter. It consumes the Conversation Log's Signal, estimates the size of the carried
Instructions, and re-emits that same signal (Instructions intact) on one of two outputs:

```
Conversation Log.Signal → Token Threshold ─ Under Limit ────────────────→ LLM Call
                                   └ Over Limit → Compactor ─────→ LLM Call
```
Both branches carry the Instructions, so **every turn reaches the LLM Call exactly once** (consume-once
on the LLM Call's Signal input dedupes), and compaction runs only on over-budget turns. Set the Threshold
to ~80% of the model's context window. Needs a synchronous estimator (Heuristic/Tiktoken). The async
**Summarizer**, if placed inline without a gate, should self-gate (pass through unchanged under a
threshold) so it does not fire an LLM call every turn.

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
