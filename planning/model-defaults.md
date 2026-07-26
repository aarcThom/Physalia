# Known Model Defaults — Design Guidelines

**Authoritative** for `src/Physalia.Core/Models/Defaults/` (`AnthropicModelDefaults`,
`OpenAIModelDefaults`, `GeminiModelDefaults`). Read this before adding or editing a model
entry. Written 2026-07-11.

## Purpose

Models must work correctly **out of the box** — a user who drops a Model component on the
canvas and wires nothing into a Tweaker gets a request the provider accepts and, where the
model reasons, visible thinking. Every per-model quirk (which thinking form a model
accepts, whether it rejects sampling parameters, which token-limit key it wants) lives in
exactly one place per wire protocol: the Defaults class. These classes are **living
documents** — when a provider ships or changes a model, the table is what gets edited.

## The three-layer contract

Every request is shaped by three layers. Keep them separate:

1. **User intent** lives on the config record, and the "not specified" state is real:
   thinking fields are nullable (`AnthropicProtocolConfig.ThinkingBudget: int?`,
   `OpenAIProtocolConfig.ThinkingEnabled: bool?`, `GeminiProtocolConfig.ThinkingBudget: int?`).
   `null` means *auto* — the user said nothing. Explicit values come from Tweaker inputs
   and always win. An unwired optional Tweaker input must leave the field `null`, never
   write a default into it.
2. **Known behaviour** lives in the Defaults class: a small ordered pattern table mapping
   model-family name fragments to a behaviour `Entry` record, plus a conservative
   `Fallback` for unknown models.
3. **The merge** happens in the provider's request builder (`BuildRequestBody` /
   `BuildGenerationConfig`) and nowhere else: resolve the entry, apply explicit intent
   mapped to what the model accepts, fall back to the entry's defaults when intent is
   `null`.

Nothing else in the codebase may branch on a model name. Not GH components, not LlmCall,
not the parsers. If you feel the need to check a model ID outside a Defaults class, the
behaviour you are encoding belongs in the table as a new `Entry` field.

## Principles

1. **Map intent, never pass it through blindly.** If the user asks for thinking in a form
   the model rejects, send the form it accepts: a manual budget wired into an
   adaptive-only model (Sonnet 5) becomes `{type:"adaptive"}`; the adaptive sentinel wired
   into a manual-only model (Sonnet 4.5) becomes `{type:"enabled", budget_tokens:8192}`.
   A 400 caused by a thinking/sampling field is a bug in our table, not a user error.
2. **Make billed thinking visible by default.** If a model thinks by default (Sonnet 5,
   Fable, Gemini 2.5+, and the API bills for it regardless), default to requesting the
   visible form (`display:"summarized"` / `includeThoughts:true`) — invisible billed
   thinking is the worst of both worlds and was the original "empty signal" bug. But if a
   model's thinking is **off** by default, never enable it silently: that adds cost and
   latency the user didn't ask for (Opus 4.7/4.8, Sonnet 4.6, DeepSeek non-thinking
   models... with one deliberate exception: DeepSeek V4 defaults thinking **on** because
   reasoning is that family's purpose and the non-thinking variant is the surprising
   choice).
3. **Explicit "off" is best-effort.** Tweaker `0`/`false` sends the disable form where the
   model supports one; where it doesn't (Fable/Mythos cannot disable thinking), send no
   thinking config at all — display stays omitted, which is the closest available
   approximation. Never turn an "off" request into an error.
4. **The fallback is conservative.** Unknown models get the oldest widely-compatible
   behaviour: no thinking fields, no optional request fields that older APIs or local
   servers (llama.cpp) might reject, sampling parameters allowed. When in doubt, the
   fallback omits; only table rows opt in.
5. **One table per wire protocol, not per vendor.** DeepSeek, OpenRouter, Groq, and
   llama.cpp all ride `OpenAIModelDefaults`; a new Anthropic-compatible endpoint rides
   `AnthropicModelDefaults`. A new vendor on an existing protocol means new *rows*, not a
   new class.

## Pattern-matching rules

- Matching is **case-insensitive substring** against the model ID, ordered
  **most-specific first** — `"mythos-preview"` must precede `"mythos"`, and family
  patterns must not collide across generations (`"sonnet-5"` does not match
  `claude-sonnet-4-5`; verify this property for every new pattern you add).
- Substring (not equality) so date-suffixed IDs (`claude-sonnet-5-20260201`) and vendor
  prefixes keep matching.
- `OpenAIModelDefaults` strips an OpenRouter-style `provider/` namespace before matching,
  and matches the o-series (`o1`/`o3`/`o4`) by **prefix**, not substring — substring would
  false-match unrelated names. Follow that split: `ContainsModels` for distinctive family
  names, `PrefixModels` for short ambiguous ones.
- First match wins. There is no merging across rows — each row carries the complete
  profile for its family.

## What an Entry encodes (and what it doesn't)

Entries encode **request-shaping behaviour**: thinking forms and defaults, sampling
parameter tolerance, token-limit key names, required opt-in fields. They do **not** encode
descriptive metadata — context length, pricing, vision/tool support belong to
`ModelEntry`/`ModelInformation` (display and estimation), not here. Keep the two concerns
separate.

The registry also only controls the **request** side. The response side — wrapping
thinking deltas as inline `<think>…</think>`, stripping them from resent history
(`ThinkingTags`), surfacing `StopReason` — is uniform per protocol and lives in the
parsers. Do not add per-model response handling without a very good reason and matching
parser tests.

## Checklist: adding or updating a model family

1. **Read the provider's docs** (fetch the live page — this moves fast) and determine:
   which thinking form(s) the model accepts, whether thinking runs by default, whether it
   can be disabled, whether visible thinking needs an explicit opt-in, whether sampling
   parameters (temperature/top_p/top_k) are rejected, and which token-limit key it wants.
2. **Add or edit the row** in the right Defaults class, respecting pattern order. Never
   delete rows for deprecated models — old IDs linger in saved documents and compatible
   proxies, and a stale-but-correct row is harmless.
3. **Update the class `<remarks>`**: source URL and "checked YYYY-MM-DD" date. The remark
   is the provenance trail for the next editor.
4. **Add request-body tests** in `src/Physalia.Core.Tests/Providers/…RequestBodyTests.cs`
   — one per *behaviour* the row triggers (default-on thinking, explicit off, form
   mapping, sampling omission), driven through `BuildRequestBody` with the real model ID.
   Also assert the pattern doesn't collide with a neighbouring generation when the names
   are close.
5. **If the model introduces a new axis of behaviour**, add a field to the `Entry` record
   with a default that reproduces current behaviour for every existing row, extend the
   provider merge logic, and test both values. Do not encode the new axis as a special
   case in the provider.
6. `dotnet build src/Physalia.slnx -c Debug` and `dotnet test` — both must stay clean.

## Current axes (2026-07-11)

- **Anthropic** (`Entry`: `SupportsAdaptive`, `SupportsManualBudget`,
  `ThinkingOnByDefault`, `SupportsDisabled`, `AllowsSampling`, `SupportsEffort`): the
  generational split is Sonnet 4.6 / Opus 4.6 and earlier (manual budget, sampling OK) vs
  Opus 5 / Sonnet 5 / Opus 4.7+ / Fable / Mythos (adaptive-only, sampling rejected on
  *every* request, thinking display defaults to `"omitted"` so visible thinking requires
  `display:"summarized"`). **Opus 5** (added 2026-07-25) is adaptive-only and thinks *by
  default* — the break from Opus 4.7/4.8, where omitting the field meant no thinking — so
  it takes `ThinkingOnByDefault: true` like Sonnet 5, and its `max_tokens` must cover
  reasoning plus the answer. One Opus 5 constraint is deliberately **not** in the table:
  `thinking:{type:"disabled"}` is accepted only at effort `"high"` or below and 400s at
  `"xhigh"`/`"max"`. The builder hard-codes `"medium"` and never sends effort alongside a
  disabled thinking config, so the pairing is unreachable; if an effort input is ever
  exposed on the Tweaker, that becomes a new `Entry` axis (a max-effort-when-disabled
  cap), not a special case in the provider.
  `ThinkingBudget` intent: `null` auto, `0` off, `-1` adaptive+summarized, `>0` manual
  budget (clamped to the 1024 API minimum; `max_tokens` auto-bumped to exceed it).
  `SupportsEffort` (4.6+ generations): adaptive thinking is sent with
  `output_config:{effort:"medium"}` to temper the server default (`"high"`), which
  over-reasons in pipeline loops and eats the shared `max_tokens` budget — the 2026-07-13
  live-session truncation failures were exactly this. Default `max_tokens` when nothing is
  wired is 32768 (thinking and answer share it); the manual-form default thinking budget
  stays 8192, deliberately decoupled so the extra headroom goes to the answer.
- **OpenAI protocol** (`Entry`: `ThinkingOnByDefault`, `UsesMaxCompletionTokens`,
  `AllowsSampling`): DeepSeek V4 needs `thinking:{type:"enabled"}` before it emits
  `reasoning_content`; o-series/GPT-5 need `max_completion_tokens` and reject sampling.
- **Gemini** (`Entry`: `IncludeThoughtsByDefault`): 2.5+/3 think by default but return
  thought text only with `thinkingConfig.includeThoughts:true`; older models reject
  `thinkingConfig` entirely.

## Known limitations (documented, deliberately unsolved)

- Anthropic thinking + tool-use rounds want thinking blocks echoed back with signatures;
  inline `<think>` tags can't carry them and history is stripped on resend.
- DeepSeek thinking + tool calls similarly wants `reasoning_content` echoed back.
- Both degrade gracefully (the model loses its chain of thought between tool rounds); fix
  would require a structured thinking carrier, which the signal carrier discipline
  intentionally forbids — revisit only with a full design.
