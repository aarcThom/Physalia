# LLM API Parameter Reference
## Physalia Multiplatform Agent

> Covers OpenAI Chat Completions, Anthropic Messages, and Google Gemini `generateContent`.  
> Parameters known to you — `messages`, `system`, `role` — are omitted; this doc focuses on everything else.

---

## 1. OpenAI Chat Completions API

`POST https://api.openai.com/v1/chat/completions`

### Core Generation

| Parameter | Type | Default | Description |
|---|---|---|---|
| `model` | string | — | Model ID to use (e.g. `gpt-4o`, `o3`). Determines capability ceiling, pricing, and context window. |
| `temperature` | float | `1.0` | Sampling temperature. Higher = more random output, lower = more deterministic. Range `0.0–2.0`. Mutually exclusive with `top_p` in practice. |
| `top_p` | float | `1.0` | Nucleus sampling. The model considers only the top tokens whose cumulative probability mass reaches this threshold. Range `0.0–1.0`. |
| `max_tokens` / `max_completion_tokens` | int | model max | Hard cap on tokens the model may generate. `max_completion_tokens` is the newer name and includes reasoning tokens for o-series models. |
| `n` | int | `1` | How many independent completion candidates to generate per request. Costs `n × tokens` against your quota. |
| `seed` | int | `null` | If set, the API makes a best-effort attempt to sample deterministically so repeated requests with the same seed and parameters return the same output. |
| `stop` | string \| array | `null` | Up to 4 sequences at which the model stops generating. The stop string itself is not included in the output. |

### Penalty & Bias Controls

| Parameter | Type | Default | Description |
|---|---|---|---|
| `presence_penalty` | float | `0.0` | Penalises tokens that have *appeared at all* in the text so far, nudging the model toward new topics. Range `-2.0–2.0`. |
| `frequency_penalty` | float | `0.0` | Penalises tokens proportional to their *frequency* in the text so far, reducing verbatim repetition. Range `-2.0–2.0`. |
| `logit_bias` | object | `null` | Map of token ID → bias value (`-100–100`). Applied to logits before sampling. `-100` effectively bans a token; `100` nearly forces it. |

### Probability Inspection

| Parameter | Type | Default | Description |
|---|---|---|---|
| `logprobs` | bool | `false` | Whether to include log-probabilities for the generated tokens in the response. |
| `top_logprobs` | int | `null` | When `logprobs` is true, how many top-token log-probabilities to return at each position. Range `0–20`. |

### Structured Output & Format

| Parameter | Type | Default | Description |
|---|---|---|---|
| `response_format` | object | `{type: "text"}` | Controls output shape. Options: `text`, `json_object` (JSON mode — model constrained to valid JSON), or `json_schema` (structured outputs — model constrained to your schema). |

### Tool Use

| Parameter | Type | Default | Description |
|---|---|---|---|
| `tools` | array | `null` | List of tool definitions the model may call. Each tool has a `type` (`function`), `name`, `description`, and JSON Schema `parameters`. |
| `tool_choice` | string \| object | `"auto"` | Controls tool invocation. `"none"` disables tools, `"auto"` lets the model decide, `"required"` forces a call, or `{"type":"function","function":{"name":"..."}}` forces a specific tool. |
| `parallel_tool_calls` | bool | `true` | Whether the model may emit multiple tool calls in a single response turn. Set to `false` to force sequential single-tool calls. |

### Reasoning (o-series models only)

| Parameter | Type | Default | Description |
|---|---|---|---|
| `reasoning_effort` | string | `"medium"` | For o-series reasoning models. Controls how much internal chain-of-thought the model performs. Values: `"low"`, `"medium"`, `"high"`. Higher = better reasoning, more latency and token cost. |

### Streaming

| Parameter | Type | Default | Description |
|---|---|---|---|
| `stream` | bool | `false` | If true, tokens are sent as server-sent events as they are generated rather than in a single response. |
| `stream_options` | object | `null` | Additional streaming config. Currently supports `include_usage: true` to append a final chunk with token usage stats. |

### Operational

| Parameter | Type | Default | Description |
|---|---|---|---|
| `user` | string | `null` | A stable opaque identifier for the end-user. Used by OpenAI for abuse monitoring. Not sent to the model. |
| `service_tier` | string | `"auto"` | Request priority tier. `"auto"` uses the highest tier available to your account; `"default"` uses standard capacity. |
| `store` | bool | `false` | Whether to store the completion for use in distillation and evals dashboards. |
| `metadata` | object | `null` | Arbitrary key-value pairs attached to the stored completion for filtering in the dashboard. Only meaningful when `store: true`. |

---

## 2. Cross-Provider Parameter Equivalence

The table maps each OpenAI parameter to its Anthropic Messages API and Google Gemini `generateContent` equivalents.  
**✗** = no direct equivalent. **~** = partial or approximate equivalent.

| OpenAI Parameter | Anthropic (`/v1/messages`) | Gemini (`generateContent`) | Notes |
|---|---|---|---|
| `model` | `model` | `model` (in URL path) | All three use a model ID string; naming conventions differ. |
| `temperature` | `temperature` | `generationConfig.temperature` | Anthropic range is `0.0–1.0`; OpenAI is `0.0–2.0`; Gemini is `0.0–2.0`. Normalise on intake. |
| `top_p` | `top_p` | `generationConfig.topP` | Semantically identical across all three. |
| `top_k` | `top_k` | `generationConfig.topK` | **OpenAI has no `top_k`.** Both Anthropic and Gemini support it natively. |
| `max_tokens` / `max_completion_tokens` | `max_tokens` (**required**) | `generationConfig.maxOutputTokens` | Anthropic requires this field; OpenAI and Gemini do not. |
| `n` | ✗ | `generationConfig.candidateCount` | Anthropic does not support multiple candidates. Gemini supports it but billing is per candidate. |
| `seed` | ✗ | `generationConfig.seed` | Anthropic has no seed parameter. Gemini supports it for determinism. |
| `stop` | `stop_sequences` (array only) | `generationConfig.stopSequences` | Anthropic and Gemini accept only arrays. Anthropic calls the field `stop_sequences`. |
| `presence_penalty` | ✗ | `generationConfig.presencePenalty` | Anthropic has no penalty controls. |
| `frequency_penalty` | ✗ | `generationConfig.frequencyPenalty` | Anthropic has no penalty controls. |
| `logit_bias` | ✗ | ✗ | Not supported by either Anthropic or Gemini. |
| `logprobs` | ✗ | `generationConfig.responseLogprobs` | Gemini has partial support. Anthropic does not expose log-probabilities. |
| `top_logprobs` | ✗ | `generationConfig.logprobs` (int) | Gemini's `logprobs` int param sets how many top tokens to return per position. |
| `response_format` | `output_format` *(beta)* | `generationConfig.responseMimeType` + `responseSchema` | Anthropic structured outputs are a beta feature. Gemini uses a MIME type + JSON schema pair. |
| `tools` | `tools` | `tools` (via `FunctionDeclaration`) | All three use a tool/function-definition schema, but the exact schema shape differs. |
| `tool_choice` | `tool_choice` | `toolConfig.functionCallingConfig` | Anthropic mirrors OpenAI fairly closely (`auto`, `any`, specific tool). Gemini uses `mode: AUTO / ANY / NONE` inside `toolConfig`. |
| `parallel_tool_calls` | ✗ | ✗ | No direct equivalent. Gemini can return multiple function calls; Anthropic can too, but neither has a toggle. |
| `reasoning_effort` | `thinking` object (`budget_tokens` or `effort`) | `generationConfig.thinkingConfig` (`thinkingBudget`) | Anthropic uses `{"type":"enabled","budget_tokens":N}` or `effort` string. Gemini uses a token budget integer. All are model-gated. |
| `stream` | `stream` | Use `streamGenerateContent` endpoint | Anthropic uses the same `stream` boolean. Gemini switches to a different endpoint for streaming. |
| `stream_options` | ✗ | ✗ | No equivalent. Anthropic returns usage in the final `message_delta` event natively. |
| `user` | `metadata.user_id` | ✗ | Anthropic places this inside a `metadata` object. Gemini has no user-tracking field. |
| `service_tier` | `service_tier` (`"standard"` \| `"priority"`) | ✗ | Anthropic has an analogous priority capacity field. Gemini handles this at the project/quota level. |
| `store` / `metadata` | ✗ | ✗ | Anthropic and Gemini have no storage flag on the request itself. |
| *(no OAI equiv)* | `top_k` | `generationConfig.topK` | Unique to Anthropic and Gemini. Limits the sampling pool to the top-K tokens before `top_p` is applied. |
| *(no OAI equiv)* | `system` (top-level string or array) | `systemInstruction` (content object) | Anthropic requires the system prompt at the top level, not inside messages. Gemini uses a `systemInstruction` content block. |
| *(no OAI equiv)* | `cache_control` (on content blocks) | `cachedContent` (resource name) | Prompt caching. Anthropic uses inline `cache_control` breakpoints. Gemini pre-creates a `CachedContent` resource and references it by name. |
| *(no OAI equiv)* | `betas` (array of strings) | ✗ | Anthropic-specific opt-in header for beta features (e.g. structured outputs, extended context). |
| *(no OAI equiv)* | `container` | ✗ | Anthropic-specific: container reuse for code execution environments. |
| *(no OAI equiv)* | `context_management` | ✗ | Anthropic-specific: rules for automatically clearing function results or thinking blocks when context grows. |

---

## 3. OpenRouter

`POST https://openrouter.ai/api/v1/chat/completions`

OpenRouter is a **drop-in superset of the OpenAI Chat Completions API**. Any code targeting OpenAI works against OpenRouter by changing the base URL and API key — the full OpenAI parameter set is accepted as-is. OpenRouter then routes the request to whichever upstream provider serves that model (OpenAI, Anthropic, Google, Meta, Mistral, etc.).

```python
# Using the OpenAI SDK — only the base URL changes
from openai import OpenAI

client = OpenAI(
    base_url="https://openrouter.ai/api/v1",
    api_key="<OPENROUTER_API_KEY>",
)
```

Two optional request headers identify your app on the OpenRouter leaderboard — they have no effect on inference:

| Header | Description |
|---|---|
| `HTTP-Referer` | Your app's URL, used for attribution on openrouter.ai rankings. |
| `X-OpenRouter-Title` | Your app's display name for the same rankings. |

### OpenRouter-specific parameters

These are additional fields OpenRouter accepts **on top of** the standard OpenAI body:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `models` | string[] | — | Ordered list of model IDs to attempt as fallbacks. If the primary `model` fails (rate-limit, downtime, content filter), OpenRouter tries each entry in sequence. Response is billed at the rate of whichever model was actually used. |
| `provider` | object | — | Controls which upstream providers are eligible to serve this request. See sub-fields below. |
| `provider.order` | string[] | — | Ordered list of provider slugs to try (e.g. `["anthropic", "openai"]`). Disables load balancing. |
| `provider.only` | string[] | — | Whitelist — allow only these provider slugs for this request. |
| `provider.ignore` | string[] | — | Blacklist — skip these provider slugs for this request. |
| `provider.allow_fallbacks` | bool | `true` | If `false`, fail hard rather than trying backup providers when the primary is unavailable. |
| `provider.require_parameters` | bool | `false` | Only route to providers that support every parameter in your request body (e.g. won't route to a provider that ignores `response_format`). |
| `provider.data_collection` | string | `"allow"` | `"deny"` excludes providers that may store prompts or completions for training. |
| `provider.zdr` | bool | — | Restrict routing to Zero Data Retention endpoints only. |
| `provider.sort` | string \| object | — | Sort eligible providers by `"price"`, `"throughput"`, or `"latency"` rather than the default price-weighted load balancing. Pass an object `{"by": "throughput", "partition": "none"}` to sort globally across model fallbacks. |
| `provider.preferred_min_throughput` | number \| object | — | Soft preference for minimum tokens/sec. Providers below this threshold are deprioritised but not excluded. Accepts a plain number or percentile cutoffs `{p50, p75, p90, p99}`. |
| `provider.preferred_max_latency` | number \| object | — | Soft preference for maximum time-to-first-token in seconds. Same format as above. |
| `provider.max_price` | object | — | Hard price ceiling. Requests are rejected (not just deprioritised) if no provider can serve the model under this price. |
| `provider.quantizations` | string[] | — | Filter to providers running specific quantisation levels, e.g. `["fp8", "int8"]`. Useful for open-weight models where multiple quantisations are available. |

### Model slug shortcuts

OpenRouter supports two suffixes on any `model` string as convenience aliases for `provider.sort`:

| Suffix | Equivalent to |
|---|---|
| `model-id:nitro` | `provider: { sort: "throughput" }` — always route to the highest-throughput provider. |
| `model-id:floor` | `provider: { sort: "price" }` — always route to the cheapest provider. |

### Anthropic beta features via OpenRouter

When routing to Anthropic models, OpenRouter passes through the `anthropic-beta` header, enabling Anthropic beta features without needing to call Anthropic directly:

```json
{
  "model": "anthropic/claude-sonnet-4-5",
  "messages": [...],
  "provider": {
    "only": ["anthropic"]
  },
  "x-anthropic-betas": ["interleaved-thinking-2025-05-14"]
}
```

### Physalia implications

For Physalia, OpenRouter is effectively a **fourth adapter** at the routing layer rather than the inference layer. The recommended approach is:

- Keep your OpenAI adapter as-is and point its base URL at `https://openrouter.ai/api/v1`.
- Expose `models` (fallback list) and the `provider` routing object as optional Physalia config that is passed through in `extra_body` when using the OpenAI SDK.
- Model IDs on OpenRouter are namespaced: `anthropic/claude-sonnet-4-5`, `openai/gpt-4o`, `google/gemini-2.0-flash`, etc. — surface this naming convention in your model picker.

---

## 4. OpenCode Zen

`https://opencode.ai/zen/v1/`

OpenCode Zen is a **curated AI gateway** maintained by the OpenCode team. Unlike OpenRouter — which exposes a single unified OpenAI-compatible endpoint for all models — Zen exposes **native per-provider endpoints**, meaning each model family speaks its own protocol. It is not a drop-in replacement for any single provider; it is closer to a credentials aggregator with a hand-picked, benchmarked model list.

> OpenCode Zen is currently in beta.

### How it works

Sign in at `opencode.ai/auth`, add billing details, copy your API key. Each request is charged per token at Zen's published rates (a thin margin over provider cost). The full model list is available at `GET https://opencode.ai/zen/v1/models`.

### Endpoint routing by model family

This is the key architectural difference from OpenRouter. Each model family uses a **different base URL and a different wire format**:

| Model family | Base URL | Wire format | SDK |
|---|---|---|---|
| OpenAI GPT models | `https://opencode.ai/zen/v1/responses` | OpenAI Responses API | `@ai-sdk/openai` |
| Anthropic Claude models | `https://opencode.ai/zen/v1/messages` | Anthropic Messages API | `@ai-sdk/anthropic` |
| Google Gemini models | `https://opencode.ai/zen/v1/models/<model-id>` | Gemini `generateContent` | `@ai-sdk/google` |
| Others (Qwen, MiniMax, Kimi, GLM, etc.) | `https://opencode.ai/zen/v1/chat/completions` | OpenAI Chat Completions | `@ai-sdk/openai-compatible` |

There are no Zen-specific parameters — you send the native payload for that model family exactly as you would to the upstream provider, with your Zen API key substituted in the `Authorization` header.

### Curated model list (as of April 2026)

| Provider | Models |
|---|---|
| OpenAI | GPT 5.4 / Pro / Mini / Nano, GPT 5.3 Codex / Spark, GPT 5.2, GPT 5.1 Codex / Max / Mini, GPT 5, GPT 5 Codex / Nano |
| Anthropic | Claude Opus 4.7 / 4.6 / 4.5 / 4.1, Claude Sonnet 4.6 / 4.5 / 4, Claude Haiku 4.5 / 3.5 |
| Google | Gemini 3.1 Pro, Gemini 3 Flash |
| Qwen | Qwen3.6 Plus, Qwen3.5 Plus |
| MiniMax | MiniMax M2.7, M2.5 (paid + free tier) |
| Kimi | Kimi K2.5, K2.6 |
| GLM | GLM 5.1, GLM 5 |
| Other | Ling 2.6 Flash, Hy3 Preview, Nemotron 3 Super, Big Pickle (all currently free) |

### Privacy

Most models are zero-retention by default. Notable exceptions:

- **OpenAI models** — retained for 30 days per OpenAI's data policy.
- **Free-tier models** (Big Pickle, MiniMax M2.5 Free, Ling 2.6 Flash Free, Hy3 Preview Free, Nemotron 3 Super Free) — data may be used to improve those models during the free period. Not suitable for sensitive prompts.

### Zen vs OpenRouter — comparison

| | OpenRouter | OpenCode Zen |
|---|---|---|
| **API surface** | Single unified OpenAI-compatible endpoint | Native endpoint per model family |
| **Model catalogue** | Hundreds of models | Hand-picked ~35 models |
| **Selection rationale** | Availability + price | Benchmarked for coding agent quality |
| **Custom routing params** | ✓ (`provider` object, fallbacks, sorting) | ✗ (no routing controls) |
| **Model fallbacks** | ✓ (`models` array) | ✗ |
| **Zero data retention** | Configurable (`provider.zdr`) | Default for most models |
| **Team management** | Enterprise tier | ✓ Built-in (free during beta) |
| **BYOK** | ✓ | ✓ |
| **Pricing model** | Per token (pass-through + margin) | Per token (pay-as-you-go) |

### Physalia implications

Zen is **not a single new adapter** — it is a set of base URL overrides applied to your existing adapters:

```csharp
// Pseudocode — Zen just changes the base URL per provider family
var zenOpenAI   = new OpenAIAdapter(baseUrl: "https://opencode.ai/zen/v1/responses",  apiKey: zenKey);
var zenAnthropic = new AnthropicAdapter(baseUrl: "https://opencode.ai/zen/v1/messages", apiKey: zenKey);
var zenGemini   = new GeminiAdapter(baseUrl: "https://opencode.ai/zen/v1/models",      apiKey: zenKey);
var zenCompat   = new OpenAIAdapter(baseUrl: "https://opencode.ai/zen/v1/chat/completions", apiKey: zenKey);
```

The practical value for Physalia is access to the non-OpenAI/Anthropic/Google models (Qwen, MiniMax, Kimi, GLM) through a single paid account, without needing separate API keys for each. If you already have direct provider keys for OpenAI, Anthropic, and Gemini, the only additive value Zen provides is access to those fourth-tier models and the curated quality guarantee.

---

## 5. DeepSeek

`POST https://api.deepseek.com/chat/completions`  
`POST https://api.deepseek.com/beta/chat/completions` *(for Chat Prefix Completion and FIM)*

DeepSeek is an **OpenAI Chat Completions-compatible API** — the same base URL swap approach works. The standard endpoint accepts the full OpenAI parameter set. There are, however, meaningful deviations around thinking mode, temperature guidance, a unique `reasoning_content` field in responses, and two beta-endpoint-only features (prefix completion and FIM) that have no OpenAI analogues.

```python
from openai import OpenAI

client = OpenAI(
    api_key="<DEEPSEEK_API_KEY>",
    base_url="https://api.deepseek.com",  # or .../v1 for OpenAI compat alias
)
```

### Models (current as of April 2026)

| Model ID | Description |
|---|---|
| `deepseek-v4-pro` | Full DeepSeek V4. 1M context. Supports thinking and non-thinking modes. |
| `deepseek-v4-flash` | Smaller/faster V4. 1M context. Supports thinking and non-thinking modes. |
| `deepseek-chat` | Alias → currently routes to `deepseek-v4-flash` (non-thinking mode). Retiring Jul 24 2026. |
| `deepseek-reasoner` | Alias → currently routes to `deepseek-v4-flash` (thinking mode). Retiring Jul 24 2026. |

Use the versioned IDs (`deepseek-v4-pro`, `deepseek-v4-flash`) for any new integrations — the legacy aliases are being retired.

### DeepSeek-specific parameters

All standard OpenAI Chat Completions parameters are accepted. The following are additions or significant behavioural deviations:

| Parameter | Type | Default | Description |
|---|---|---|---|
| `thinking` | object | disabled | Enables thinking (chain-of-thought) mode for any model. Pass `{"type": "enabled"}`. Alternative to using the `deepseek-reasoner` model alias. When using the OpenAI SDK, pass via `extra_body={"thinking": {"type": "enabled"}}`. |
| `reasoning_effort` | string | — | Controls thinking depth. Values: `"low"`, `"medium"`, `"high"`. Maps to OpenAI's same parameter — safe to pass directly. |

### Temperature guidance

DeepSeek publishes explicit task-based temperature recommendations, which differ from OpenAI defaults:

| Task type | Recommended temperature |
|---|---|
| Coding / math | `0.0` |
| Data cleaning / analysis | `1.0` (default) |
| General conversation / translation | `1.3` |
| Creative writing | `1.5` |

The range is `0.0–2.0`, matching OpenAI. No normalisation needed when forwarding from your OpenAI adapter.

### Thinking mode constraints

When thinking mode is active (`thinking: {type: "enabled"}` or model = `deepseek-reasoner`), the following parameters are **silently ignored** (accepted for compatibility, no effect): `temperature`, `top_p`, `presence_penalty`, `frequency_penalty`. Setting `logprobs` or `top_logprobs` in thinking mode will trigger a **400 error** — explicitly drop those before forwarding.

### `reasoning_content` in responses

In thinking mode, the response message contains an extra field alongside `content`:

```json
{
  "choices": [{
    "message": {
      "role": "assistant",
      "content": "The final answer is...",
      "reasoning_content": "Let me think through this step by step..."
    }
  }]
}
```

**Critical multi-turn behaviour:** In thinking + tool-use loops, `reasoning_content` from intermediate sub-turns must be passed back to the API in subsequent requests so the model can continue its reasoning chain. At the start of a new user turn (not a tool result), `reasoning_content` should be stripped from history to save bandwidth. If `reasoning_content` is incorrectly retained when starting a new turn, the API will return a 400 error.

### Context caching

DeepSeek implements **automatic disk-based KV caching** — no explicit cache control markup is required. The cache hit is reflected in the usage fields of the response: `prompt_cache_hit_tokens` and `prompt_cache_miss_tokens`. Cache hits are charged at a significantly reduced rate. This is unlike Anthropic (explicit breakpoints) and Gemini (pre-created resource) — no action is needed in your request body.

### Beta endpoint features

Available at `https://api.deepseek.com/beta/chat/completions`:

| Feature | Parameter | Description |
|---|---|---|
| **Chat Prefix Completion** | Prefill the assistant message | Pass a partial `assistant` message as the last turn to force the model to continue from that prefix. Equivalent to Anthropic's assistant-turn prefill pattern. |
| **FIM Completion** | `prompt`, `suffix` | Fill-in-the-middle for code completion. Send a `prompt` (code before the cursor) and `suffix` (code after), and the model fills the gap. No OpenAI or Anthropic equivalent. |

### Anthropic API compatibility

DeepSeek also exposes an Anthropic-format endpoint:

```
ANTHROPIC_BASE_URL=https://api.deepseek.com/anthropic
```

This lets you point an existing Anthropic SDK client at DeepSeek. Unsupported Anthropic parameters are silently ignored. Useful if Physalia's Anthropic adapter needs a DeepSeek fallback without writing a separate adapter.

### DeepSeek vs OpenAI — parameter delta summary

| Parameter | OpenAI behaviour | DeepSeek behaviour |
|---|---|---|
| `temperature` | Always effective, `0.0–2.0` | Ignored in thinking mode; same range otherwise |
| `top_p` | Always effective | Ignored in thinking mode |
| `presence_penalty` / `frequency_penalty` | Always effective | Ignored in thinking mode |
| `logprobs` / `top_logprobs` | Always effective | **Error** in thinking mode |
| `thinking` | ✗ | ✓ `{"type": "enabled"}` to activate CoT |
| `reasoning_effort` | o-series only | ✓ Supported on all DeepSeek models |
| `reasoning_content` (response) | ✗ | ✓ Present in thinking mode responses |
| Context caching | ✗ (Chat Completions) | ✓ Automatic, no request markup needed |
| FIM completion | ✗ | ✓ Beta endpoint only |
| Prefix completion | ✗ | ✓ Beta endpoint only |

### Physalia implications

- **Wire DeepSeek through your OpenAI adapter** with a base URL swap. It handles the standard parameter set identically.
- **Thinking mode interop:** The `thinking` object and `reasoning_effort` are already in your OpenAI param model — pass them through as `extra_body` when targeting DeepSeek.
- **Drop `logprobs`/`top_logprobs` before forwarding to thinking-mode requests** — they cause hard errors, unlike OpenAI where they're always valid.
- **`reasoning_content` in history management:** If you build generic conversation memory, you'll need a DeepSeek-aware pass that strips or retains `reasoning_content` depending on whether the next message is a tool result or a new user turn.
- **Implicit caching is a free win** — no adapter changes needed, just surface the cache hit token counts from the usage field if you expose cost tracking in Physalia.

---

## 6. Ollama

`POST http://localhost:11434/api/chat` *(native)*  
`POST http://localhost:11434/v1/chat/completions` *(OpenAI-compatible)*  
`POST http://localhost:11434/v1/messages` *(Anthropic-compatible)*

Ollama is a **local model runtime** — it runs quantised open-weight models on your own hardware (CPU, Apple Silicon, NVIDIA GPU) with no cloud dependency. It exposes three wire formats on the same server: a native API with llama.cpp-level controls, an OpenAI-compatible layer, and an Anthropic Messages-compatible layer. For Physalia's purposes, the OpenAI-compat path is the lowest-friction entry point, but the native API unlocks parameters that don't exist anywhere else.

```python
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:11434/v1/",
    api_key="ollama",  # required by the SDK, ignored by Ollama
)
```

No API key is required by default. Set the `OLLAMA_API_KEY` environment variable on the server if you want to enable auth for a networked deployment.

### Models

Models are identified by `name:tag` (e.g. `llama3.2:latest`, `qwen3:8b`, `deepseek-r1:7b`). Pull before use:

```bash
ollama pull qwen3:8b
```

Browse available models at `ollama.com/library`. For tooling that hardcodes OpenAI model names, alias any local model:

```bash
ollama cp llama3.2 gpt-3.5-turbo
```

---

### OpenAI-compatible endpoint (`/v1/chat/completions`)

Ollama's OpenAI compat layer accepts a curated subset of the OpenAI parameter set. Unsupported parameters are silently ignored rather than erroring.

**Supported parameters (as of current release):**

`model`, `messages`, `temperature`, `top_p`, `max_tokens`, `stop`, `stream`, `stream_options`, `seed`, `presence_penalty`, `frequency_penalty`, `response_format`, `tools`, `tool_choice`, `logit_bias`, `logprobs`, `top_logprobs`, `user`, `n`, `reasoning_effort`, `reasoning.effort`

**Notable omissions vs. full OpenAI spec:** `parallel_tool_calls`, `store`, `metadata`, `service_tier`. These are silently dropped.

**Context size** cannot be set via the OpenAI compat API. It must be baked into the model at creation time via a Modelfile:

```
FROM llama3.2
PARAMETER num_ctx 8192
```

---

### Native API (`/api/chat`)

The native endpoint is the more expressive interface. It exposes llama.cpp sampling parameters that have no equivalent in any cloud provider's API.

#### Top-level parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `model` | string | — | Model name, e.g. `qwen3:8b`. |
| `messages` | array | — | Same role/content structure as OpenAI. |
| `tools` | array | — | Function tool definitions. Same schema as OpenAI. |
| `format` | string \| object | — | `"json"` for JSON mode, or a JSON Schema object for structured output. |
| `stream` | bool | `true` | Streaming is **on by default** in the native API, unlike all cloud providers where it defaults to false. |
| `think` | bool \| string | — | Enables chain-of-thought for supported models. `true`/`false`, or `"high"` / `"medium"` / `"low"` for effort level. Exposes `thinking` field in response. |
| `keep_alive` | string \| number | `"5m"` | How long to keep the model loaded in RAM after the request. `"0"` unloads immediately; `"-1"` keeps it loaded indefinitely. Critical for latency when Physalia makes rapid sequential calls. |
| `logprobs` | bool | `false` | Return log-probabilities for output tokens. |
| `top_logprobs` | int | — | Number of top-token log-probabilities per position when `logprobs` is true. |

#### `options` object — llama.cpp sampling parameters

These are passed as a nested `options` object and have no equivalent in any cloud provider API:

| Option | Type | Description |
|---|---|---|
| `num_ctx` | int | Context window size in tokens. The primary way to control context length in the native API. |
| `num_predict` | int | Max tokens to generate. Equivalent to `max_tokens`. `-1` = unlimited, `-2` = fill context. |
| `temperature` | float | Sampling temperature. |
| `top_p` | float | Nucleus sampling threshold. |
| `top_k` | int | Limits sampling to top-K tokens before applying `top_p`. |
| `min_p` | float | Minimum token probability relative to the most likely token. Alternative to `top_p`. |
| `seed` | int | RNG seed for reproducible outputs. |
| `stop` | string[] | Stop sequences. |
| `repeat_penalty` | float | Penalises recently repeated tokens. Roughly equivalent to `frequency_penalty`. |
| `repeat_last_n` | int | How many tokens back to consider for repeat penalty. |
| `tfs_z` | float | Tail-free sampling parameter. Reduces impact of low-probability tokens. No cloud equivalent. |
| `typical_p` | float | Locally typical sampling. Alternative to `top_p`. No cloud equivalent. |
| `mirostat` | int | Enables Mirostat sampling (`1` or `2`). Dynamically targets a desired perplexity level instead of using fixed `top_p`/`top_k`. No cloud equivalent. |
| `mirostat_tau` | float | Mirostat target entropy. Controls output "surprise" level. |
| `mirostat_eta` | float | Mirostat learning rate. |
| `num_gpu` | int | Number of GPU layers to offload. |
| `num_thread` | int | CPU thread count for generation. |

#### Response timing fields

The native API response includes inference timing metadata not present in any cloud API:

| Field | Description |
|---|---|
| `total_duration` | Total wall time in nanoseconds. |
| `load_duration` | Time spent loading the model (0 if already loaded). |
| `prompt_eval_count` | Input token count. |
| `prompt_eval_duration` | Time spent processing the prompt in nanoseconds. |
| `eval_count` | Output token count. |
| `eval_duration` | Time spent generating tokens in nanoseconds. |

---

### Ollama vs cloud providers — key differences

| | Cloud providers | Ollama |
|---|---|---|
| **Hosting** | Remote, API key required | Local, no key by default |
| **Model selection** | Provider's hosted catalogue | Any GGUF/Ollama-format model you pull |
| **Context size control** | Request parameter | Modelfile at model creation time (native API) or `options.num_ctx` |
| **Streaming default** | `false` | `true` (native API only) |
| **Sampling depth** | temperature, top_p, top_k (varies) | Full llama.cpp stack: mirostat, tfs_z, typical_p, min_p, repeat_last_n |
| **Timing metadata** | Usage tokens only | Nanosecond timing per phase |
| **Model keep-alive** | N/A (stateless) | Configurable in-process memory retention |
| **Cost** | Per token | Hardware electricity only |
| **Image input** | Base64 or URL (varies) | Base64 or URL (OpenAI compat); base64 inline in native |
| **Files API** | Some providers | ✗ None |

---

### Physalia implications

- **Use the OpenAI compat path for the common case.** It works with your existing OpenAI adapter via base URL swap. The API key value is irrelevant — just pass any non-empty string.
- **Expose `keep_alive` as a Physalia config option.** The difference between `"0"` (unload after each call) and `"-1"` (keep loaded) is significant latency on repeated inference. For an interactive agent loop like Physalia, `"5m"` or `"-1"` is almost always correct.
- **`num_ctx` requires a Modelfile round-trip** on the OpenAI compat path — it cannot be set per-request. If Physalia needs dynamic context windows against Ollama, it must use the native API's `options.num_ctx` instead.
- **Streaming is on by default in the native API** — the inverse of every cloud provider. If you share a streaming abstraction across adapters, make sure the Ollama native path explicitly sets `stream: false` when you don't want streaming, not the other way around.
- **The `think` parameter** maps cleanly to `reasoning_effort` on the OpenAI compat path, giving you thinking control on local models like `deepseek-r1` or `qwen3` without any adapter changes.
- **Mirostat and tfs_z** are meaningful for creative/generative tasks in Grasshopper — worth surfacing in a Physalia "advanced sampling" panel for the Ollama provider if you ever expose it to power users.

---

## 7. Image Input  

No separate upload call is required for the common case — all three providers support **inline delivery** directly inside a message content block. All three also expose a **Files API** for upload-once-reuse-many workflows.

### Delivery methods

#### OpenAI

Accepts a public URL or a `data:` URI with base64, both inside an `image_url` content block. The optional `detail` field controls token cost vs. resolution (`"low"`, `"high"`, `"auto"`). The Files API exists but is scoped to Assistants and fine-tuning — it is **not** available for Chat Completions.

```json
{
  "role": "user",
  "content": [
    {
      "type": "image_url",
      "image_url": {
        "url": "https://example.com/image.jpg",
        "detail": "high"
      }
    },
    {
      "type": "image_url",
      "image_url": {
        "url": "data:image/jpeg;base64,/9j/4AAQ..."
      }
    },
    { "type": "text", "text": "What is in this image?" }
  ]
}
```

#### Anthropic

Uses a typed `source` object inside an `image` content block. Supports three source types: `base64` (inline bytes), `url` (server-side fetch, no upload needed), and `file` (reference to a prior Files API upload by `file_id`).

```json
{
  "role": "user",
  "content": [
    {
      "type": "image",
      "source": { "type": "base64", "media_type": "image/jpeg", "data": "/9j/4AAQ..." }
    },
    {
      "type": "image",
      "source": { "type": "url", "url": "https://example.com/image.jpg" }
    },
    {
      "type": "image",
      "source": { "type": "file", "file_id": "file_abc123" }
    },
    { "type": "text", "text": "What is in this image?" }
  ]
}
```

The `file_id` comes from a separate `POST /v1/files` call using `multipart/form-data`. Files persist indefinitely (no TTL) and can be referenced across multiple requests and sessions — the right pattern for a static reference image used repeatedly in a Physalia session.

#### Gemini

Uses `inlineData` for base64 and `fileData` for a URI reference. The URI can come from the Gemini Files API or a Google Cloud Storage path (`gs://...`). Unlike Anthropic and OpenAI, Gemini does **not** support fetching arbitrary public URLs inline — images must be base64 or accessed via a GCS/Files API URI.

```json
{
  "role": "user",
  "parts": [
    {
      "inlineData": {
        "mimeType": "image/jpeg",
        "data": "/9j/4AAQ..."
      }
    },
    {
      "fileData": {
        "mimeType": "image/jpeg",
        "fileUri": "https://generativelanguage.googleapis.com/v1beta/files/abc123"
      }
    },
    { "text": "What is in this image?" }
  ]
}
```

The Gemini Files API (`POST /upload/v1beta/files`) stores assets for **48 hours** with automatic expiry. No longer-term persistence is available.

---

### Image delivery comparison

| | OpenAI | Anthropic | Gemini |
|---|---|---|---|
| **Inline base64** | ✓ (`data:` URI in `image_url.url`) | ✓ (`source.type: "base64"`) | ✓ (`inlineData`) |
| **Public URL** | ✓ (`image_url.url`) | ✓ (`source.type: "url"`) | ✗ (GCS or Files API URI only) |
| **Files API reference** | ✗ (not for Chat Completions) | ✓ (`source.type: "file"`) | ✓ (`fileData.fileUri`) |
| **Files API persistence** | N/A | Indefinite | 48-hour TTL |
| **Reuse across requests** | Re-send base64/URL each time | ✓ via `file_id` | ✓ via `fileUri` (within 48h) |
| **Resolution hint** | ✓ `detail: "low"/"high"/"auto"` | ✗ | ✗ |
| **Separate upload call needed** | Never (for Chat Completions) | Optional | Optional |

---

### ImageSource abstraction for Physalia

The key design question is whether your `ImageSource` type distinguishes **inline** (bytes/URL passed directly) from **managed** (an ID or URI from a prior upload). A minimal C# discriminated union:

```csharp
public abstract record ImageSource;
public record InlineImage(byte[] Data, string MimeType) : ImageSource;
public record UrlImage(string Url) : ImageSource;           // OpenAI + Anthropic only
public record ManagedImage(string ProviderId, string FileHandle) : ImageSource;
```

Then each provider adapter maps `ManagedImage` to:
- Anthropic → `source.type: "file"`, `file_id: FileHandle`
- Gemini → `fileData.fileUri: FileHandle`
- OpenAI → reject or fall back to inline (no Files API on Chat Completions path)

The upload itself lives in an explicit `IImageUploader.UploadAsync()` that the caller invokes before constructing the message, keeping the completion call stateless.

---

## 8. Quick-Reference: Physalia Integration Notes

- **Temperature normalisation:** Clamp OpenAI-style `0–2` input to `0–1` before forwarding to Anthropic.  
- **`max_tokens` is mandatory on Anthropic.** Always inject a sensible default if the caller omits it.  
- **`top_k`** is a free capability win on Anthropic and Gemini — expose it in Physalia's config even though OpenAI ignores it.  
- **Multiple candidates (`n`):** Only Gemini supports this natively. For Anthropic, parallelise `n` separate requests.  
- **Structured output:** Treat this as a beta feature gate per provider; check the `betas` array on Anthropic requests.  
- **Streaming on Gemini** requires hitting `streamGenerateContent` rather than toggling a boolean — route accordingly.  
- **Penalty params** are OpenAI/Gemini-only; silently drop them when targeting Anthropic rather than erroring.  
- **Image URLs:** Anthropic and OpenAI accept arbitrary public URLs inline; Gemini requires GCS or Files API URIs — you'll need a separate upload step for Gemini if you can't guarantee a GCS-hosted asset.  
- **Image persistence:** Only Anthropic's Files API stores assets indefinitely. Gemini's 48h TTL means you can't treat uploaded files as permanent references in long-running workflows.  
- **OpenRouter as a fifth path:** Wire it through your existing OpenAI adapter with a base URL swap. Expose `models` and `provider` as passthrough config rather than modelling them in your core abstraction — they are routing concerns, not inference concerns.  
- **OpenCode Zen is not one adapter, it's four base URL overrides.** Its value is primarily access to non-major-provider models (Qwen, MiniMax, Kimi, GLM) under a single key. If you already have direct keys for OpenAI, Anthropic, and Gemini, only add Zen when you want those fourth-tier models.
- **DeepSeek wires through the OpenAI adapter** with a base URL swap, but requires two special-case behaviours: (1) drop `logprobs`/`top_logprobs` before forwarding to thinking-mode requests or you'll get a hard 400; (2) `reasoning_content` in multi-turn history must be managed differently depending on whether the next message is a tool result or a new user turn.
- **Ollama uses the OpenAI compat path** for the common case. Critical callouts: `keep_alive` should default to `"5m"` or longer for agent loop use; `num_ctx` cannot be set per-request on the compat path and requires a Modelfile; streaming defaults to `true` on the native API (inverse of all cloud providers); the native `options` object unlocks llama.cpp sampling parameters (mirostat, tfs_z, min_p) with no cloud equivalent.
