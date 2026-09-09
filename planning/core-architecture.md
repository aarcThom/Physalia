# Core architecture — namespaces, conversation model, providers, credentials

> Split out of `CLAUDE.md` (2026-09-08) to keep that file under its size limit. Content is verbatim; CLAUDE.md links here.

## Core Architecture

### Boundary Rule
`Physalia.Core` is a pure functional library — no side effects, no GH dependency. GH owns all mutable state.

### Namespace Structure (actual)
```
Physalia.Core/
    Common/          ← Result<T,E>, LlmError, LlmErrorKind, LlmResponseChunk, LlmUsage,
                       LlmToolCall, HttpErrorMapper, StringHelpers
    Config/          ← Api (YAML key file parsing), ApiKey, LlmProviderFactory
    ConvoInstruct/   ← Role, MessageContent, ImageSource, ConversationMessage,
                       Conversation, ConversationHelpers, Instructions
    Models/          ← ModelConfig (abstract), ModelEntry, ModelList
        Protocol/    ← OpenAIProtocolConfig, AnthropicProtocolConfig, GeminiProtocolConfig (abstract records)
        Named/       ← OpenAICompatibleConfig, AnthropicConfig, GeminiConfig
    Providers/       ← ILlmProvider, ProtocolProviderBase (HttpClient + shared request/stream helpers)
        OpenAiProtocol/, Anthropic/, Gemini/  ← protocol providers (per-provider wire-format parsing)
        Named/       ← OpenAICompatibleProvider, AnthropicProvider, GeminiProvider
    Signals/         ← PhySignal, SignalOutcome, SignalSequencer
    Tokens/          ← ITokenEstimator + estimators, AsyncTokenEstimation, TokenEstimationHelpers
    Validation/      ← SchemaValidator, ValidationError, JsonExtractor
```

### Conversation Model (`ConvoInstruct/`)
```csharp
public enum Role { User, Assistant }  // Tool added when tool-calls land

public abstract record MessageContent;
public record TextContent(string Text) : MessageContent;
public record ImageContent(ImageSource Source) : MessageContent;

public abstract record ImageSource;       // InlineImage, UrlImage, ManagedImage

public record ConversationMessage(Role Role, IReadOnlyList<MessageContent> Content);
public record Instructions(string SystemPrompt, Conversation Conversation);
```

- `Conversation` is a **class** (not record) — `Append()` returns a new `Conversation`, enforces invariants (no consecutive same-role turns); `MergeIntoLastUserMessage()` handles user-side text when the last turn is already a user message (providers require strict role alternation).
- `Instructions` bundles conversation + system prompt for one inference call (Conversation Log → LLM Call).
- Images travel inside `ConversationMessage`, not as a side-channel.

### Provider Hierarchy
Abstract classes (not interfaces) — share `HttpClient` state via `ProtocolProviderBase`.
```
ProtocolProviderBase
    OpenAIProtocolProvider    → OpenAICompatibleProvider
    AnthropicProtocolProvider → AnthropicProvider
    GeminiProtocolProvider    → GeminiProvider
```
- `ProtocolProviderBase` owns HttpClient, `TryGetConfig<T>`, `SendStreamingRequestAsync`, `SendForStringAsync`, `ReadStreamLineAsync`, `ParseModelIdsFromDataArray`. **Wire-format/SSE parsing stays per-protocol provider** — do not merge it.
- `ModelConfig` hierarchy mirrors the provider hierarchy. DeepSeek/Ollama/OpenRouter/Groq etc. ride `OpenAICompatibleProvider` via base-URL swap, not separate classes.
- **A local llama.cpp server is one of those base-URL swaps, and nothing more** — 2026-09-09. It had a `LlamaCppProvider` and a `LlamaCppConfig` for a while; both are DELETED. The provider was empty (its own doc comment said "no overrides are needed") and never reached the factory, which mapped `LlamaCppConfig` onto `OpenAICompatibleProvider` anyway. The config's only remaining job was carrying the default `http://127.0.0.1:8080/v1`, which `ProviderCatalog` already owned — so that literal now lives once, as `ProviderCatalog.LocalLlmEndpoint`, read by the setup page's Detect probe and by the LlamaCpp API component. **Do not re-add a named class for llama.cpp**: what a local server needs is an address, not a wire format. `LlamaCppServerQuery` (Tokens) stays — asking a server how much context it was started with is genuinely llama.cpp-specific, and it takes any `OpenAIProtocolConfig`.
- `HttpErrorMapper.MapStatusCode` is the single HTTP-status → `LlmErrorKind` source.

### Provider Interface
```csharp
IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(
    Conversation conversation, string systemPrompt, ModelConfig config, CancellationToken ct);
```

### Result / Error Types
```csharp
Result<T, E>   // rolled our own (.Ok / .Err nested records), no external dependency
public record LlmError(LlmErrorKind Kind, string Message);
public enum LlmErrorKind { Network, Auth, RateLimit, InvalidRequest, Timeout, Cancelled }
public record LlmResponseChunk(string? ContentDelta, bool IsLast, LlmUsage? Usage,
                               IReadOnlyList<LlmToolCall>? ToolCalls = null);
public record LlmUsage(int InputTokens, int OutputTokens);
```

### Validation (Schema Validator)
Pure functions: `JsonExtractor.ExtractJson/PrettyPrint` (strip LLM prose / markdown fences) and `SchemaValidator.Validate(string json, string schema) → Result<string, ValidationError>`.

### Credentials — endpoint + key, encrypted, UI-owned (reworked 2026-09-04; `planning/model-api-credentials.md`)

**A key and its endpoint are ONE fact.** `ModelApi(Provider, BaseUrl, Key)` replaced the old
`ApiKey`, and the `Model API` component (was "API Keys") emits both on one wire — which is why
`OpenAICompatibleModel` no longer has a `Base URL` input. Alibaba, Z.AI and Moonshot are all
OpenAI-compatible at *different* hosts, so a key on its own identifies nothing.

**Providers are configured in the chat window**, which writes them to
`%LOCALAPPDATA%/Physalia/credentials.dat` — DPAPI-encrypted for the current user, beside the MCP
token cache. As of 2026-09-09 that folder holds far more than secrets: `SecretStores.DataFolder()`'s
reasoning ("it sits in the install directory where a plug-in update can overwrite it") turned out to
apply to everything the user writes, so project folders, memories and saved presets moved there too
behind `PhyData` — see `planning/project-files-and-phy.md`. That is affordable **only because the UI owns authoring**: nobody hand-edits the store,
so nothing is lost by making it opaque. The inverse is the reason a plain-text config file could
never have been encrypted instead — being openable in a text editor was its entire purpose, which is
also why it had to go rather than be hardened.

**Availability is not consent.** A key in the environment, or a CLI on PATH, says a provider *could*
be used — never that the user wants Physalia spending that quota. `ProviderActivation`
(`%LOCALAPPDATA%/Physalia/providers.json`, **plain JSON, deliberately not encrypted** — it holds no
secrets, stays readable, and survives a credential store that cannot be decrypted) is the opt-in
list, and `Resolve` returns null for anything not on it however available it is. `StatusFor` is the
un-gated view the setup page needs, so a found key can be *offered* ("found in `GEMINI_API_KEY` — add
to Physalia") rather than either ignored or silently adopted. Before this, a machine with unrelated
tooling installed arrived pre-wired to providers nobody had chosen.

`ModelApiResolver` is the single read path (Model API component, `WebToolKeys`, `ProviderAvailability`),
and it has exactly **two** credential sources:
1. **Environment variable** — first. No credential on disk at all beats any encryption, and it is the
   headless/CI/team path. Names live in `ProviderCatalog`.
2. **The encrypted store.**

**There is no file-based fallback.** `API_KEY_CONFIG.YAML`, its `.example`, its parser (`Api.cs`) and
the one-time importer are all **deleted** (2026-09-04) — with no released version to migrate from,
a plain-text YAML was simply a second way to configure the same thing, and two of those disagree
eventually. Don't reintroduce one: a provider needing a non-default endpoint (Alibaba's regions, a
Z.AI Coding Plan key, a private gateway) is configured in the chat window, and the YAML had nowhere
to put an endpoint at all.

The endpoint and the key resolve **independently**, so a shell-managed token still picks up a custom
endpoint from the store.

- **`ProviderCatalog` is the one vocabulary** — ids shared by the store, the resolver, the bridge
  verbs and the UI's `providers.ts`. It replaced two mapping tables (`ChatWindow.KeyTargets`,
  `ProviderAvailability.KeyProviderToSetupId`) that had to agree with nothing enforcing it. **Keep it
  in step with `providers.ts`**: that file owns the setup prose, this owns the wiring.
- **`ISecretStore` (`Config/Secrets/`) is the ONLY platform seam.** `DpapiSecretStore` on Windows,
  `FileSecretStore` (plaintext + owner-only mode) elsewhere; **macOS Keychain is one new class plus
  one line in `SecretStores.For`** and nothing above it changes. DPAPI is our own ~40-line P/Invoke
  (`WindowsDataProtection`), byte-compatible with `ProtectedData` — **zero new package references**,
  and the MCP bridge shares it by **linked compile** (it is a leaf net8.0 exe with no ProjectReference
  to Core, deliberately), which is what keeps ONE DPAPI implementation in the repo.
- **`Unreadable` is not `Empty`, and the distinction is load-bearing.** A store written by another
  Windows account decrypts to nothing; reporting "no providers configured" there sends the user off
  to re-enter keys that are sitting right in front of them. Saving over an unreadable store is
  refused outright — it would discard every other provider its real owner had.
- **Reads are cached** (3s + explicit invalidate). The Model API node re-resolves every solve to keep
  its Picker live; without the cache that is a DPAPI decrypt per solve per node.
- **`ModelApiResolver` takes an injected environment lookup.** Reading the real environment made the
  resolution order untestable — a dev box with `OPENAI_API_KEY` set failed a test about Tavily.
- **API keys are never serialized into GH files** (`GH_ModelApi.Write/Read` and
  `GH_ModelConfig.Write/Read` are intentional no-ops); `GH_ModelApi` casts out only to the label
  `"<provider> api"`, never to the key or the URL.
- **Setup page shape — one footer per provider, chosen by its `ProviderStatus`:** *connected* → the
  reconfigure form (endpoint + key, key providers only) plus **Disconnect**; *available but not
  connected* → exactly ONE button ("Key found in `GEMINI_API_KEY` — add to Physalia", "Connect
  Claude Code"); *nothing found* → the **API URL** + **API key** form, or a **Detect** button for a
  probed provider. Tool keys (Tavily, Jina) have no endpoint, so no URL box. **Saving a typed key
  activates it** — typing it IS the opt-in; only a credential Physalia merely *found* needs a second
  act. Detection results are still never stored: `ProviderAvailability` re-probes, so an uninstalled
  CLI drops out on its own.
- **A configured provider is REACHABLE, and that is what makes the connected footer worth having**
  (2026-09-06). Its pill on the picker opens its page and carries a pencil to say so; before that it
  was a plain `<span>` label, so a connected provider was the one thing on that screen with no way
  back into it — a rotated key could not be pasted, a moved endpoint could not be corrected, and a
  connection could not be switched off at all. The footer had been written and was unreachable.
- **Reconfiguring and disconnecting are different acts and neither is the other's side effect.**
  Editing takes a **blank key box to mean "keep the stored key"** (`ProviderStatus.HasStoredKey` +
  `BaseUrl` are pushed; the KEY never is), so an endpoint-only edit cannot silently destroy a
  credential — the same contract as the API endpoints page. **Disconnect** is the forget verb: it
  deactivates AND removes the stored entry, so it is not worded as a toggle, and it asks a **second
  time only when a key is actually on disk** — a subscription CLI loses nothing by being switched
  back on, and a confirmation nobody needs is one everybody clicks through. An ENVIRONMENT key is
  never Physalia's to delete, so that case says so and goes straight through. The endpoint box
  prefills from `BaseUrl`, the endpoint **in effect** rather than the catalog default, or reopening
  an Alibaba region / Z.AI Coding Plan host offers the wrong one back for saving.
- **Every connected provider can be switched off, Claude Code and Codex included.** They store
  nothing, so there is no key to forget — but the connection IS the consent, and it is spending a
  subscription. `ProviderActivation.Deactivate` is the whole mechanism.

---

