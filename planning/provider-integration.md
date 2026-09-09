# Provider integration notes

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## Provider Integration Notes (API research — see `planning/api_research.md`)

### Known model defaults registry (design guidelines: `planning/model-defaults.md` — read before touching)
- Per-model quirks (thinking forms, sampling rejection, token-limit key names) live **only** in `Physalia.Core/Models/Defaults/` (`AnthropicModelDefaults` / `OpenAIModelDefaults` / `GeminiModelDefaults`) — ordered pattern tables consulted by the request builders. **Never branch on a model name anywhere else.**
- Three-layer contract: nullable config thinking fields carry user intent (`null` = auto → registry default; explicit Tweaker values win, **mapped** to the form the model accepts — a rejected thinking/sampling field is a table bug, not user error). Unknown models get a conservative fallback (omit optional fields).
- Default philosophy: models that think-and-bill by default get *visible* thinking automatically (`display:"summarized"` / `includeThoughts`); thinking that is off by default is never silently enabled.
- Thinking rides inline as `<think>…</think>` in streamed text (chat UI renders it; `ThinkingTags` strips it from resent assistant history); truncation surfaces via `LlmResponseChunk.StopReason` → LLM Call warning. The registry shapes **requests only** — response parsing stays uniform per protocol.

### Temperature
- Anthropic range: `0.0–1.0`. OpenAI/Gemini/DeepSeek: `0.0–2.0`. **Clamp/normalise on intake for Anthropic.** Newest Anthropic generations (Sonnet 5 / Opus 4.7+ / Fable) reject non-default temperature/top_p/top_k on every request — the registry omits them there; OpenAI reasoning models (o-series/GPT-5) likewise reject sampling and require `max_completion_tokens` instead of `max_tokens`.
- `max_tokens` is **required** on Anthropic — always inject a default.

### Provider-as-adapter pattern
- DeepSeek, Ollama, OpenRouter, Groq: `OpenAICompatibleProvider` + base URL swap.
- DeepSeek thinking mode: drop `logprobs`/`top_logprobs` before forwarding (hard 400 error); manage `reasoning_content` in history depending on next turn type.
- Ollama: `keep_alive` default `"5m"` or `"-1"` for agent loops; native API streams by default (inverse of cloud providers).
- OpenRouter model IDs are namespaced: `anthropic/claude-sonnet-4-6`, `openai/gpt-4o`, etc.

### Image Delivery
- OpenAI + Anthropic: accept arbitrary public URLs inline. Gemini requires GCS or Files API URI.
- Anthropic Files API: indefinite persistence. Gemini Files API: 48h TTL.
- `ImageSource` discriminated union: `InlineImage`, `UrlImage`, `ManagedImage` — each adapter maps to provider format.

### Local-CLI providers (warm process, no API key)
Two providers do inference by driving a CLI the user already signed into, so no key is stored or
sent: **Claude Code** (`Providers/ClaudeCode`, `claude`) and **Codex** (`Providers/Codex`, `codex`).
They share a shape, and it is the shape to copy for any future one:
- **One warm process per LLM Call**, pooled on `ModelConfig.SessionKey` (the LLM Call stamps its
  `InstanceGuid`); an idle reaper kills abandoned sessions, `ProcessExit` kills them all, and
  `LlmCall.RemovedFromDocument` calls **both** providers' `EndSession`.
- **Seed then delta.** The first turn sends the whole history serialised into one user message; after
  that the CLI holds the context, so only the newest user turn goes over. Anything that is not a
  clean one-user-message extension of what the session absorbed forces a fresh process — as does a
  changed model or system prompt, both of which are fixed at process/thread start.
- **A seed is text PLUS its images** (`ConversationHelpers.ToSeedContent`, shared by both CLI
  providers), never text alone. Rendering the history as a string turns a picture into
  `[Image: image/png, N bytes]` — the model is told an image exists and shown nothing — so a snapshot
  was silently invisible on exactly the turns that reseed, which is most of them in a real pipeline:
  a tool round, a feedback turn, a compaction and a cold process all grow the conversation by more
  than one user message. The transcript text is split around each image so the picture stays in the
  turn that carried it; inline and URL images ride as real blocks, while a `ManagedImage` keeps its
  text label, since a CLI cannot resolve another provider's file handle. A single-message
  conversation still seeds with its raw blocks. The resend cost is the one the HTTP providers pay
  every call.
- **A plain text generator, not an agent**: the CLI's own tools are switched off, the workspace is an
  empty temp dir so nothing auto-discovers, and Physalia's system prompt REPLACES the agent's base
  prompt (`--system-prompt-file` / `baseInstructions`).
- **Physalia's own tools**: Claude Code ignores the `tools` argument entirely. **Codex advertises
  them** as `dynamicTools` and hands a call back on the final chunk for the Router — see below; the
  canvas is wired exactly as it is for the HTTP providers.
- **Thinking rides inline as `<think>…</think>`**, exactly as on the API path — and on both CLIs it
  must be ASKED for, or the deltas arrive empty (Claude Code: `--thinking-display summarized`;
  Codex: `summary: "auto"` on `turn/start`). Measured, not assumed.
- Where they differ: Claude Code speaks its own NDJSON over `--input-format stream-json`; Codex
  speaks **JSON-RPC 2.0 over `codex app-server --stdio`** — `initialize` → `initialized` →
  `thread/start` (once) → `turn/start` per turn, streaming `item/agentMessage/delta` +
  `item/reasoning/summaryTextDelta` until `turn/completed`. Regenerate its protocol schema any time
  with `codex app-server generate-json-schema --out <dir>`. A server-initiated JSON-RPC *request*
  (approvals, tool calls) is answered with a `-32601` error — not ignored, or the turn stalls
  forever waiting on a reply. Codex also answers `model/list` live, so its model list is fetched
  rather than hard-coded; its detail lives in memory note `codex-provider`.

### Codex tool calls — deferred, never executed in the turn (`codex-dynamic-tools`)
Codex is the only CLI provider that can call Physalia's LLM Tools, and it does so **without any
change to how a canvas is wired** — Router, tool nodes, Feedback, Collector all behave as they do on
the HTTP providers. The trick is that a tool call is *deferred*, not serviced:
- `thread/start` declares the `tools` argument as **`dynamicTools`** (`{type:"function", name,
  description, inputSchema}` — a 1:1 match for `LlmToolDefinition`). It is an EXPERIMENTAL field, so
  `capabilities.experimentalApi` is opted into **only when there are tools**, keeping the plain
  text path on the conservative handshake it was verified with. The declared set is fixed at thread
  start, so changing it starts a new session.
- The model's call arrives as an `item/tool/call` **server request**, which blocks the turn until it
  is answered. Physalia answers `success:false` with text saying the call was deferred and its
  result will arrive in the next user message, fires `turn/interrupt`, and hands the call back on
  the final chunk as `LlmResponseChunk.ToolCalls` — from there the ordinary Router loop runs it.
- **Everything the model says after a tool call is dropped** (text, reasoning, the completed
  message). It is a reaction to the deferral — an apology or an offer to retry — and the interrupt
  does NOT reliably land before a sentence escapes, so the tail is discarded rather than raced for.
  What survives is the run-up, which makes the assistant turn preamble + tool_use, exactly what the
  HTTP providers produce.
- **The session stays warm across a tool round.** A tool-call turn counts as consumed, so the
  results come back as a plain one-message delta — no reseed, no cold start. They ride as TEXT
  (`[Tool result: id:…]`, worded to match `ConversationHelpers`), because the model's call was
  already answered inside its own turn; there is no open call left to satisfy.
- Codex issues calls **sequentially** (one answered before the next is made), where Anthropic can
  emit several in one turn — so a multi-tool question costs more rounds here, not more wiring.

---

