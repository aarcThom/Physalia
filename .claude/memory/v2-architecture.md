# Physalia v0.2 Planned Architecture
Source: `planning/physalia-primitives.md` + `planning/api_research.md`

## Design Philosophy
v0.2 decomposes the monolithic System Prompt/Transmitter/Receiver into a proper pipeline with strict separation of concerns. Each component owns exactly one responsibility.

---

## Core Pipeline (linear, left-to-right)

### System Prompt
- Assembles system prompt from discrete inputs: `preamble`, `schema`, `tool descriptions`
- Output: `system prompt` (string)
- Right-click: Save/append `.composer` file
- **Deterministic**

### Prompter
- Sole human input entry point — owns the chat UI (Eto panel) and trigger
- Inputs: UI interface, `Submit` button, `reference` (file path, optional), `inputs outputs` (from Monitor, optional)
- Outputs: `prompt` (string), `reference` (passthrough), `trigger` (bool, fires on submit)
- Right-click: `Open Chat` — Eto chat window as alternative UI
- **Deterministic**

### Conversation Log
- Append-only conversation log; sole source of truth for LLM Call
- Inputs: `system prompt` (from System Prompt), `prompt` (from Prompter), `feedback` (N inputs from Feedback components), `trigger`
- Outputs: `conversation` (full history string), `reference` (passthrough), `trigger`
- Arbitrates between forward flow and incoming Feedback signals (blocks one when other is active)
- Right-click: Save/Load/Clear conversation (.convo JSON)
- **Deterministic**

### LLM Call
- Core LLM inference — stateless; all context lives in Conversation Log
- Inputs: `instructions` (from Conversation Log), `model` (Model record), `reference` (file path, optional), `trigger`, `cancel` (button)
- Outputs: `response` (raw LLM string), `trigger`, `feedback` (same as response if successful, null if API call failed)
- Alternate use cases via Component Catalog: Distiller, Reflector, Interpreter, Encoder, Curator, Critic, Translator, Educator
- **LLM-driven**

### Schema Validator
- Strips non-essential content from LLM response; validates JSON against schema (NJsonSchema or JsonSchema.Net)
- Inputs: `data` (raw LLM output), `schema` (from Component Catalog), `trigger`
- Outputs: `data` (clean JSON), `trigger`, `feedback` (error info, user role)
- Alternate use cases via Component Catalog: Monitor (structural connection validity)
- **Deterministic**
- ⚠️ SUPERSEDED (2026-06-04): Schema Validator now inherits `RoutingComponentBase<string>` — **no input `trigger`** (re-runs on any input change), outputs are Data / Success Trigger / Feedback / Fail Trigger (momentary pulses), and they latch. See [routing-trigger-system.md](routing-trigger-system.md). The same model will likely apply to PyValidator/Transmitter below when built.

### PyValidator
- Python-specific pre-assembly validator; sits between Schema Validator and Transmitter on Python path
- Inputs: `data` (validated Python script JSON), `trigger`
- Outputs: `data` (passthrough if valid), `trigger`, `feedback` (error info)
- Validation sequence:
  1. **Static analysis** — pyflakes via `RhinoCode.RunScript`; input vars injected as `name = None` stubs
  2. **Dry-run execution** — real `Rhino.Geometry` available; catches RhinoCommon type errors (ImportError, AttributeError)
- See dummy input table in physalia-primitives.md for typeHint→dummy value mapping
- v0.1 gaps to fix in v0.2: `IsBlockingError` fragile (use pyflakes codes), Python loaded twice (combine into ScriptValidator), no dry-run, no tree access
- **Deterministic**

### Transmitter
- Junction between correctness layer and execution layer
- Inputs: `data` (JSON from Schema Validator/PyValidator), `trigger`
- Outputs: Galapagos-style connector to Receiver, `trigger`, `feedback` (errors)
- Right-click: save/autosave receiver config, clear/detach receiver
- **Deterministic**

### Receiver
- Sole executor of side effects on GH document (component placement, param wiring, script injection)
- Two Galapagos-style connectors: one to Transmitter, one to Monitor
- Variable user inputs/outputs (set by user or LLM)
- Uses JSON to determine whether to generate Python component or cluster
- **Deterministic**

---

## Regulators

### Feedback
- Routes data wirelessly to a paired Feedback Collector (breaks GH's acyclic constraint — intentional)
- Inputs: `data`, `trigger`
- No physical output wire; "lights up" when triggered
- Visual: radio waves icon when deselected; pink wire visible when selected

### Feedback Collector
- Collects one or more wireless Feedback signals and feeds them forward
- Outputs: `data`, `trigger`

### Counter
- Blocks forward signal after N pass-throughs (prevents infinite loops)
- Inputs: `data`, `threshold` (int), `trigger`
- Outputs: `data` (passthrough while under threshold), `trigger`, `blocked` (bool)

### Meter
- Blocks forward signal when token budget exceeded (cost governance for BYOK)
- Inputs: `data`, `budget` (int, max tokens), `trigger`
- Outputs: `data`, `trigger`, `blocked` (bool)

---

## Perception

### Monitor
- Captures state of a Receiver, GH group, or entire document
- Galapagos-style wireless connection to target (no connection = entire document)
- Inputs: `trigger`
- Outputs: `data` (description string), `trigger`

### Canvas Observation
- Captures Rhino viewport screenshots
- Inputs: `target` (GH geo for camera zoom, optional), `trigger`
- Outputs: `screenshot` (file path), `trigger`

---

## Configuration

### Component Catalog
- References files in `/SKILLS` and `/SCHEMAS` folders
- Inputs: `folder` (path), `file` (value picker)
- Outputs: vary per file spec

### Model  *(replaces ProviderSelector + ModelSelector)*
- Carries everything for an LLM API call: provider, model ID, API key, inference params
- API key resolution order: (1) YAML path wired in, (2) env var, (3) Physalia global settings
- Inputs: `provider`, `model id`, `yaml path` (optional), `temperature`, `top-p`, `max tokens`
- Output: `model` (Model record)
- Internal: `public record Model(string Provider, string ModelId, string ApiKey, float Temperature, float TopP, int MaxTokens)`

---

## Utility

### Aggregator
- Combines N perception outputs into one structured observation for Conversation Log
- Inputs: `data` (N string inputs), `trigger`
- Outputs: `aggregated` (string), `trigger`

### Router
- LLM classification call; activates one of N output paths
- Inputs: `data` (N inputs), `model`, `trigger`
- Outputs: `route 1..N` (bool triggers, only selected fires), `selected` (string, for logging)
- **LLM-driven** (classification call, minimal output — suited to small local models)

---

## File Structure (bundled with plugin)

```
/Physalia
    /Runtime          (the plugin)
    /Files
        API_KEY_CONFIG.YAML
        /SKILLS       (.skill files — prompts for LLM Call)
        /SCHEMAS      (.schema files — for Schema Validator)
        /CLUSTERS     (.ghcluster files)
        /RECEIVERS    (.receiver files)
```

---

## API Integration Notes (from api_research.md)

### Provider adapter strategy
- **OpenAI**: native adapter
- **Anthropic**: native adapter (`max_tokens` mandatory; temperature range 0–1, normalise from 0–2)
- **Gemini**: native adapter (`streamGenerateContent` endpoint for streaming; no arbitrary public URL for images)
- **DeepSeek**: OpenAI adapter + base URL swap; drop `logprobs`/`top_logprobs` for thinking-mode requests (hard 400); manage `reasoning_content` in multi-turn history
- **Ollama**: OpenAI compat path; `keep_alive` should default to `"5m"`+; `num_ctx` requires Modelfile on compat path; native API streams by default (inverse of cloud)
- **OpenRouter**: OpenAI adapter + base URL swap; expose `models` (fallbacks) and `provider` routing object as passthrough config
- **OpenCode Zen**: not one adapter — four base URL overrides (one per model family); value is access to Qwen/MiniMax/Kimi/GLM under one key

### Cross-provider gotchas
- `top_k`: Anthropic + Gemini only (expose in Physalia config, OpenAI ignores)
- `presence_penalty` / `frequency_penalty`: OpenAI + Gemini only; silently drop for Anthropic
- Multiple candidates (`n`): Gemini only natively; parallelise N requests for Anthropic
- Image URLs: Anthropic + OpenAI accept arbitrary public URLs inline; Gemini requires GCS or Files API URI
- Image persistence: Anthropic Files API = indefinite; Gemini Files API = 48h TTL
- DeepSeek implicit KV caching: automatic, no request markup needed; surface cache hit tokens in cost tracking
