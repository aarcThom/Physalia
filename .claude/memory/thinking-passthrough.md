---
name: thinking-passthrough
description: API providers now surface thinking as inline <think> tags + stop-reason truncation warnings; fixes empty-payload Success signals from reasoning models
metadata: 
  node_type: memory
  type: project
  originSessionId: 24841352-9980-4486-8be3-81e83f54e1b9
---

# Thinking passthrough + empty-signal fix (2026-07-11)

**Root cause of the empty-payload signals** (signal trace 2026-07-10): Anthropic/OpenAI/Gemini providers silently dropped thinking deltas (`thinking_delta`, `reasoning_content`/`reasoning`, `thought:true` parts) AND never read stop_reason — a reasoning model burning the whole 4096-token Anthropic default on thinking yielded a clean Success with zero text. Claude Code provider worked because it sets `MAX_THINKING_TOKENS=0`.

**Fix (all landed, builds + 273 tests green, live Rhino test pending):**
- Thinking rides inline as `<think>…</think>` in ContentDelta (the chat UI's existing convention from `content.ts splitThinking` — no UI change). Per-protocol state machines in the three `ParseSseStreamAsync`s; lazy tag-open, defensive close on final chunk.
- `ThinkingTags` (Core/Common): `Strip` + `StripAssistantMessage` — assistant history resent WITHOUT think blocks (thinking-only turn → placeholder text, Anthropic 400s on empty). Known limitation: Anthropic thinking+tool rounds lose signatures (documented, not solved).
- `LlmResponseChunk` gained `StopReason`; `StopReasons.IsTruncation` covers max_tokens/length/MAX_TOKENS. LLM Call warns (Success still minted) on truncation or thinking-only responses via the RoutingResult message channel.
- Configs: `AnthropicProtocolConfig.ThinkingBudget` (0=off, min 1024, max_tokens auto-bumped +4096 headroom, temperature/top_p/top_k omitted while enabled), `OpenAIProtocolConfig.ReasoningEffort`, `GeminiProtocolConfig.ThinkingBudget` (null=omit). Anthropic MaxTokens default 4096→8192.
- GH: `ModelComponentBase`/`TweakerComponentBase` gained `RegisterAdditionalInputs`/`ApplyAdditionalInputs` (and `RegisterAdditionalParams`/`AdjustAdditional`) virtual hooks — trailing appends, layout-safe. AnthropicModel got Max Tokens (idx 2, default 8192); all three Tweakers got an optional idx-4 input (Thinking Budget / Reasoning Effort / Thinking Budget).

**Follow-up (same day, after live test — Sonnet 4.6 showed thinking, Sonnet 5/DeepSeek didn't):**
- **Sonnet 5 / Opus 4.7+ / Fable**: adaptive-thinking generation — thinking is on by default but `display` defaults to `"omitted"` (empty thinking deltas, signature only; still billed). Manual `{type:"enabled"}` is 400-rejected. Fix: `ThinkingBudget = -1` sentinel → sends `thinking:{type:"adaptive",display:"summarized"}` (sampling params omitted — these models reject non-default temp/top_p/top_k on EVERY request, thinking or not).
- **DeepSeek V4** (`deepseek-v4-flash`/`-pro`; `deepseek-chat`/`-reasoner` retire 2026-07-24): emits `reasoning_content` only when the body carries `thinking:{type:"enabled"}`. Fix: `OpenAIProtocolConfig.ThinkingEnabled` flag + optional Boolean "Thinking" (TH, idx 5) on OpenAI Compatible Tweaker. DeepSeek thinking+tool-calls requires reasoning_content echoed back (we strip — documented limitation, same class as Anthropic signatures).
- 278 tests green. Docs: platform.claude.com adaptive-thinking page; api-docs.deepseek.com thinking_mode guide.

**UI de-nesting (2026-07-12):** `AssistantTurnGroup.svelte` — native `<think>` reasoning inside the outer "Thinking" section (JSON-deliverable turns) now renders as plain muted text via a `renderItem(item, insideThinking)` snippet param instead of a nested "Reasoning" collapsible; the standalone collapsible (no-JSON turns) was renamed Reasoning→"Thinking". Rebuilt+embedded via slnx build.

**Design guidelines:** `planning/model-defaults.md` (repo root) is now **authoritative** for the registry — three-layer contract (nullable intent / registry / provider merge), intent-mapping rule (rejected form = table bug), visible-when-billed default philosophy, pattern-ordering rules, add-a-model checklist. Pointed to from CLAUDE.md (Provider Integration Notes) and each Defaults class `<remarks>`.

**Model defaults registry (same day):** per-provider "known model behaviours" tables in `Physalia.Core/Models/Defaults/` — `AnthropicModelDefaults` (thinking form adaptive/manual, on-by-default, can-disable, sampling-allowed), `OpenAIModelDefaults` (DeepSeek-V4 thinking opt-in, o-series/gpt-5 max_completion_tokens + no sampling; namespace-stripped prefix match for o1/o3/o4), `GeminiModelDefaults` (2.5+/3 includeThoughts). These are THE living documents to update as providers ship models. Config semantics: `ThinkingBudget`/`ThinkingEnabled` became nullable — null = auto (registry), explicit values = Tweaker override, mapped to the form the model accepts (manual↔adaptive) so nothing 400s. Models now show thinking with NO tweaker wired (Sonnet 5, DeepSeek V4, Gemini 2.5). 291 tests green.

Plan: `C:\Users\rober\.claude\plans\golden-pondering-mccarthy.md`. Related: [[signal-trace-widget]], [[signal-carrier-discipline]].
