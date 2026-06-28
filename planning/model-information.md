# Model Information — Research & Fix

> 2026-06-27. Research into cross-provider model metadata + the fix landed for the Model Information
> component (builds clean; live-Rhino test pending).

## The problem

The Model Information component looked up model metadata in the **LiteLLM** catalog only, with thin id
normalization (strip provider prefix, GGUF form). It missed most models because (a) LiteLLM keys often
don't match the user's exact id, and (b) it had no second source. Result: "`<id>` not found" for most
inputs.

## Research: no single endpoint is reliable — use a layered resolver

Ranked by richness of metadata: **static datasets (models.dev / litellm) ≈ OpenRouter (online) >
Anthropic native > Gemini native > Ollama / llama.cpp (local only) ≫ OpenAI native (useless — id only).**

- **OpenRouter `GET /api/v1/models`** — public, **no auth**, ~400+ models aggregated from every major
  provider, consistently rich: `context_length`, `top_provider.max_completion_tokens`, `pricing`
  (per-token strings), `architecture.input_modalities` (→ vision), `supported_parameters` (→ tools,
  reasoning, structured outputs), `canonical_slug`. The universal workhorse — usable even for non-OR
  usage by mapping to the equivalent OR id.
- **Anthropic `/v1/models`** — now rich: `max_input_tokens`, `max_tokens`, `capabilities.*` (image,
  pdf, structured_outputs, thinking). No pricing.
- **Gemini `models.get`** — `inputTokenLimit`, `outputTokenLimit`, `supportedGenerationMethods`, param
  ranges. No pricing/modality flags.
- **OpenAI `/v1/models`** — sparse (id/created/owned_by only). Useless for metadata.
- **Ollama `/api/show`**, **llama.cpp `/props`** — the *only* source for local models (context via
  `model_info["*.context_length"]` / `n_ctx`; `capabilities` array). No pricing (local = free).
- **Static fallback datasets:** models.dev `api.json` (modality + limits + cost + capability booleans,
  keyed provider→model, $/Mtok) and litellm `model_prices_and_context_window.json` (largest coverage,
  $/token). Bundle/refresh one for offline + gap coverage.

**Id normalization is the crux of high hit-rate:** OR ids are `vendor/model` and dotted
(`anthropic/claude-sonnet-4.5`); native ids are bare, dated, and hyphenated
(`claude-sonnet-4-5-20250929`). Normalize by lowercasing, stripping `:free`/`:nitro` suffixes,
stripping trailing date stamps, converting `-N-M` → `N.M`, and trying prefix-stripped + last-segment
forms. Match exact first, then canonicalized, then static-dataset.

## What was implemented

Rewrote `Physalia.Core/Models/ModelList.cs` to a **merged two-catalog resolver** (the highest-coverage
fix without per-provider auth or local-host queries):

- `FetchAsync` now fetches **OpenRouter first** (richer, current) then **LiteLLM** (gap-filler) and
  merges. Resilient: if one source is unreachable the other is still used; only a total failure errors.
- `ParseOpenRouterInto` reads the OR `data[]` array → `ModelEntry` (context_length, max_completion_tokens,
  vision from input_modalities, tools/reasoning from supported_parameters, provider from the id prefix);
  also indexes the `canonical_slug`.
- Both catalogs index each model under **all normalized lookup-key variants** (`LookupKeys` /
  `Canonicalize`): lowercased full id, provider-prefix-stripped, last path segment, and a
  date/version-canonicalized form of each. `Find` generates the same variants for the query, so the two
  always meet — e.g. Anthropic's `claude-sonnet-4-5-20250929` resolves to OR's `anthropic/claude-sonnet-4.5`.
- Kept the GGUF normalization path and `ByProvider` (now `.Distinct()` since entries are multiply-indexed).

The component's outputs are unchanged (Max Input, Max Output, Image Capable, Tool Capable); only the
data layer and matching improved. Its description now reflects the two merged catalogs.

## Future work (for true 99.9%, including local + pricing)
- **Local providers:** query Ollama `/api/show` and llama.cpp `/props` directly (needs the provider's
  base URL from the `ModelConfig`) — the only way to cover local GGUF models.
- **Native enrich:** Anthropic/Gemini `/models` for authoritative limits when a key is present.
- **Bundled static dataset:** ship/refresh models.dev `api.json` for offline + ids the catalogs miss.
- **More outputs:** pricing ($/Mtok) and a Reasoning-capable flag (OR already provides both; `ModelEntry`
  carries `SupportsReasoning` already — just not surfaced yet).

## Sources
OpenRouter `/api/v1/models` (+ docs); Anthropic models-list; OpenAI models list; Gemini models API;
Ollama API; llama.cpp server README; models.dev (`api.json`); litellm
`model_prices_and_context_window.json`.
