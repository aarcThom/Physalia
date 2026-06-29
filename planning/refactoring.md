# Physalia Refactoring Plan

> **Implementation status (2026-06-29).** This document is the full 12-phase review. We scoped it into
> **Tier 1** (high-ROI, low-risk, behavior-preserving) and **Tier 2** (deferred structural churn).
> **Tier 1 is implemented in the working tree** (built clean, 96 Core unit tests green) but **not yet
> committed** and **not yet Rhino-verified**. Detail in memory `tier1-refactoring.md`; approved Tier-1
> plan at `.claude/plans/review-refactoring-md-under-planning-imperative-squid.md`.
>
> **Done (Tier 1):**
> - Phase 0 partial: new `tests/Physalia.Core.Tests` (xUnit, net7.0) wired into `src/Physalia.slnx`;
>   tests for Conversation, PromptImageResolver, JsonExtractor, SchemaValidator, CompactionInvariants,
>   PythonOutputAccessInference, SignalSequencer; provider streaming golden fixtures (Anthropic/OpenAI/
>   Gemini) driven through the real `ParseSseStreamAsync` via a `MemoryStream` (no HTTP).
> - Cleanups: removed the dead `update_model_info.py` PreBuild target and the unimplemented Composer
>   `.composer` menu items.
> - Phase 1 (token estimator only): `ITokenEstimator` is now a method-less marker root; new
>   `ISyncTokenEstimator` (carries `Estimate`) + `IAsyncTokenEstimator` (marker); `AsyncMarkerTokenEstimator`
>   deleted. Synchronous misuse of an API-backed estimator is now a compile error, not a runtime throw.
> - Phase 4 (the three extractions): `ConversationRecorder` (Core/Recording), `ToolDispatchRound` and
>   `ToolBatchRunner` (Core/Tools). The GH components (`Recorder`, `Router`, `ToolComponentBase`) now
>   delegate; their solve/latch/async lifecycle stayed in place.
>
> **Deferred (Tier 2, not started):** the 4-assembly split (Phase 1 packaging, Phase 2 DTOs), splitting
> `ChatWindow` (Phase 5), `GhJsonBridge` (Phase 6), the Svelte restructure (Phase 7), and Goo/Parameter
> boilerplate (Phase 10). For Core side-effect testability, the agreed approach is to **inject interfaces
> into the existing classes, not create new assemblies**.
>
> **Still required before Tier 1 is "done":** the live-Rhino sanity run in the Verification section for
> the six modified GH components.

## Scope Reviewed

This review covered the tracked source and project files in:

- `README.md`, `CLAUDE.md`, solution and project files.
- `planning/*.md`.
- `src/Physalia.Core`.
- `src/Physalia.GH`.
- `src/Physalia.UI`.

Generated or dependency directories such as `bin`, `obj`, `node_modules`, `.vs`, and `dist` were intentionally excluded. No tracked test projects were found.

## Executive Summary

Physalia already has several strong foundations: the signal model is disciplined, most conversational domain types are expressed as records or small immutable types, provider support is organized by protocol, and the Grasshopper components document a lot of non-obvious lifecycle behavior. The codebase is not low-quality. The main risk is that important policies are embedded inside large boundary classes, so the behavior is hard to test and hard to change safely.

The highest leverage refactoring is not a rewrite. It is to extract pure state machines, protocol mappers, and projection functions from Grasshopper, WebView, file-system, process, and HTTP boundaries. Once those policies are separately testable, the existing UI and component code can become thin adapters.

The biggest maintainability issues are:

1. There are no tracked tests, despite many pure algorithms and state transitions.
2. `Physalia.Core` is documented as pure domain code, but it currently owns HTTP, file-system, environment, process, CLI, and catalog-fetching concerns.
3. Several classes have accumulated too many responsibilities: `ChatWindow`, `GhJsonBridge`, `StatefulComponentBase`, `RoutingComponentBase<TData>`, `Recorder`, `Reasoner`, `Router`, and `ToolComponentBase`.
4. Provider request construction, response streaming, error mapping, tool-call parsing, and transport live in the same classes.
5. The Svelte bridge is typed on the TypeScript side, but the C# host mirrors the contract manually and routes commands through ad hoc URI parsing.
6. Async execution patterns are repeated across GH components, tools, model discovery, summarization, and provider integration.
7. Configuration and model catalog code works, but it is difficult to validate because parsing, persistence, side effects, and normalization are mixed.

The recommended direction is:

- Add tests and quality gates first.
- Preserve the current signal carrier model.
- Move side effects behind narrow adapters.
- Extract pure services from GH components before moving UI or canvas code.
- Split large boundary classes by responsibility.
- Prefer boring, idiomatic C# and Svelte patterns over clever framework-building.

## What Is Already Working Well

### Signal discipline

`PhySignal` has a clear envelope: `Payload`, `ContentBlocks`, and `Instructions`. Sequence numbers and latched consumption semantics give the GH graph a coherent event model. This is a valuable design decision and should not be diluted with more parallel carrier fields.

Keep:

- `Payload` as the human-readable summary.
- `ContentBlocks` for structured provider content.
- `Instructions` for behavioral directives.
- Sequence-based consumption.

Improve:

- Put more tests around consumption order, latching, clearing, and multi-input sequencing.
- Keep carrier expansion rare. If a new concept is needed, prefer a new `MessageContent` or instruction type before adding new top-level signal fields.

### Domain modeling

The conversation model in `Physalia.Core.ConvoInstruct` is a good example of the style the rest of the codebase should move toward. Types such as `Conversation`, `ConversationMessage`, `Instructions`, `ImageSource`, and `MessageContent` express real domain concepts instead of raw dictionaries.

Recommended refinements:

- Add constructor/factory validation for non-null lists, non-blank tool IDs, non-blank tool names, and valid image metadata.
- Add tests for `Conversation.Append`, `MergeIntoLastUserMessage`, image resolution, and tool-content pairing.
- Use the same style for provider protocol DTOs and UI bridge messages.

### Provider abstraction

`ILlmProvider` and `ProtocolProviderBase<TConfig>` give the code a useful shape. Provider-specific protocols belong in provider-specific classes, and the common streaming/full-response orchestration is not duplicated everywhere.

The next step is to separate the protocol layers inside each provider:

- Request builder.
- Streaming parser.
- Full-response parser.
- Error mapper.
- Transport.

That split would make provider behavior easy to fixture-test without hitting real APIs.

### GH component documentation

Several Grasshopper classes contain excellent comments explaining solve order, latch behavior, wireless feedback, and why work is scheduled. Preserve that explanation, but move the underlying policy into smaller units where possible.

For example, the comments in `ToolComponentBase` and `Router` explain a critical invariant: every `tool_use` ID must receive exactly one matching `tool_result`, even when a call cannot be dispatched. That invariant deserves tests and a named policy class.

## Guiding Refactoring Principles

1. Refactor around stable seams, not around file size alone.
2. Put tests around behavior before moving it.
3. Keep Grasshopper-specific code at the edges.
4. Keep provider wire formats out of UI and GH classes.
5. Replace static mutable state only when there is a clear owner for the lifecycle.
6. Prefer small records and pure functions over service objects unless a dependency must be injected.
7. Make invalid states hard to represent.
8. Do not add abstractions that only rename one method call.
9. Preserve current user-facing behavior while changing internals.
10. Let the architecture in `CLAUDE.md` become enforceable through project boundaries.

## Phase 0: Establish Tests And Quality Gates

This is the most important step. Without tests, most refactoring will rely on manual Grasshopper/Rhino verification, which makes careful cleanup too expensive.

### Add test projects

Recommended projects:

```text
tests/
  Physalia.Core.Tests/
  Physalia.GH.Tests/
  Physalia.UI.Tests/
```

`Physalia.Core.Tests` should be normal unit tests with no Rhino/Grasshopper dependency. `Physalia.GH.Tests` can initially test extracted pure policies that came from GH components; it does not need to instantiate Rhino. `Physalia.UI.Tests` can start with Vitest for pure TypeScript helpers.

Good first tests:

- `Conversation.Append` rejects consecutive same-role messages.
- `Conversation.MergeIntoLastUserMessage` preserves images and feedback flags correctly.
- `PromptImageResolver` resolves inline, URL, and managed images.
- `ConversationCompactor` modes preserve invariants through `CompactionInvariants.Reassemble`.
- `JsonExtractor` extracts fenced JSON, bare JSON, and malformed JSON predictably.
- `SchemaValidator` returns useful validation errors.
- `PythonOutputAccessInference` catches known output access patterns and ignores comments/strings where intended.
- `ComponentMatcher` confidence thresholds are stable.
- `Router` dispatch policy handles multiple calls to the same tool, missing tool outputs, and out-of-order results.
- `Recorder` turn-building policy handles prompt, assistant response, feedback, and tool-result ordering.
- `ToolComponentBase` batch policy returns one result per tool call.
- `content.ts` splits thinking blocks and large JSON consistently.

### Add fixture tests for providers

Provider bugs are expensive because they often appear only while streaming. Add golden fixtures for:

- OpenAI-compatible text deltas.
- OpenAI-compatible tool-call deltas with partial argument strings.
- OpenAI-compatible image inputs.
- Anthropic `content_block_start`, `content_block_delta`, and `content_block_stop`.
- Anthropic tool-use and tool-result formatting.
- Gemini text, images, and function calls.
- Error responses and malformed SSE events.

The target is not to mock every API. The target is to make request/response mapping deterministic.

### Add project-level gates

Recommended commands for local and CI use:

```text
dotnet build src/Physalia.slnx -c Debug
dotnet test
npm --prefix src/Physalia.UI run check
npm --prefix src/Physalia.UI run build
```

Add `.editorconfig` if one does not already exist, but do not reformat the entire repository immediately. Let new and touched code converge first.

Useful analyzer settings:

- Nullable warnings stay enabled.
- Treat unused private members as warnings.
- Prefer `ConfigureAwait(false)` in library/adapter code, but not in UI-thread code.
- Flag `async void` outside event handlers.
- Flag broad `catch` outside boundary adapters.

## Phase 1: Reassert The Core Boundary

`CLAUDE.md` says `Physalia.Core` should be a pure functional library with no Grasshopper dependency and no mutable GH state. The current project mostly follows that spirit in its domain model, but several side effects live in Core:

- File and environment access in `Physalia.Core.Config.Api`.
- HTTP catalog fetching in `Physalia.Core.Models.ModelList`.
- HTTP web tools in `Physalia.Core.Web.WebTools`.
- CLI process management and temp files in `Physalia.Core.Providers.ClaudeCode`.
- Provider HTTP transport in `ProtocolProviderBase<TConfig>`.
- Local server probing in token/model helpers.
- Project references to Grasshopper/GhJSON-related packages that should be audited.

### Recommended package boundaries

A clean target layout would be:

```text
src/
  Physalia.Core/
    Common/
    ConvoInstruct/
    Signals/
    Compaction/
    Tokens/
    Validation/
    Catalog/
    ProviderContracts/

  Physalia.Providers/
    OpenAI/
    Anthropic/
    Gemini/
    ClaudeCode/
    LlamaCpp/
    ModelCatalogs/

  Physalia.Infrastructure/
    Http/
    FileSystem/
    Secrets/
    Processes/
    Clocks/

  Physalia.GH/
    Components/
    Panels/
    Generation/
    Goo/
    Parameters/

  Physalia.UI/
```

This does not need to be created all at once. Start by moving seams, not files:

- Define interfaces in Core only when the domain needs them.
- Put implementations that touch disk, HTTP, environment variables, or processes outside Core.
- Keep GH adapters in `Physalia.GH`.

### Concrete Core extractions

#### `Physalia.Core.Config.Api`

Current issue:

- Reads environment variables.
- Reads and writes `API_KEY_CONFIG.YAML`.
- Hand-parses YAML while preserving comments.
- Is used by both Core provider code and GH setup UI.

Recommendation:

- Create an `IApiKeyStore` contract with methods such as `TryGetKey(section, leaf)` and `SetKey(section, leaf, value)`.
- Create `EnvironmentApiKeySource` and `YamlApiKeyStore` in infrastructure or GH.
- Keep key names and provider-to-section mapping as typed data, not scattered dictionaries.
- Consider using YamlDotNet, or keep the small writer but isolate it and test it against fixtures.

Target behavior:

- API keys are still never serialized into GH.
- Existing `API_KEY_CONFIG.YAML` format remains compatible.
- The first-run UI can save keys through one tested service.
- Providers can ask for a key without knowing where it came from.

#### `Physalia.Core.Models.ModelList`

Current issue:

- Fetching OpenRouter/LiteLLM catalogs, parsing JSON, normalizing model IDs, and fallback behavior are mixed in one class.

Recommendation:

- Extract `OpenRouterModelCatalogClient`.
- Extract `LiteLlmModelCatalogClient`.
- Extract `ModelIdNormalizer`.
- Store fixture JSON for each upstream format.
- Make the core model list a value model and put network refresh in an adapter.

Good tests:

- OpenRouter IDs remain namespaced.
- OpenAI-compatible aliases normalize as expected.
- GGUF/local model names are handled consistently.
- Bad or partial JSON produces a controlled fallback.

#### `Physalia.Core.Providers.ClaudeCode`

Current issue:

- Process discovery, CLI invocation, stdin/stdout parsing, temp files, session pooling, reaping, and provider mapping live in Core.

Recommendation:

- Keep the provider contract in a provider assembly.
- Move process mechanics behind `IClaudeCodeProcess` or `IProcessRunner`.
- Move temp-file and PATH probing behind small adapters.
- Test transcript parsing independently from process execution.
- Give session pooling an explicit owner with deterministic disposal.

This is the clearest violation of the current Core purity goal. It is also a high-value integration, so refactor it behind tests rather than simplifying behavior.

#### `Physalia.Core.Web.WebTools`

Current issue:

- HTTP calls to Tavily and Jina live in Core.

Recommendation:

- Keep result formatting pure and testable.
- Move HTTP clients into an adapter.
- Use request/response DTOs and fixture tests.
- Make rate limit, timeout, and API-key failures return `LlmError` consistently.

#### Token estimation

Current issue:

- `AsyncMarkerTokenEstimator` throws by design to identify estimators that require async work.

Recommendation:

- Split interfaces:

```csharp
public interface ITokenEstimator
{
    int EstimateTokens(string text);
}

public interface IAsyncTokenEstimator
{
    Task<int> EstimateTokensAsync(string text, CancellationToken cancellationToken);
}
```

Then make components choose the correct interface explicitly. Avoid marker types that succeed at compile time and fail at runtime.

## Phase 2: Provider Protocol Refactoring

The provider classes currently have the right ownership, but too many concerns live in each class:

- Build request JSON.
- Convert conversation blocks to provider-specific content.
- Stream HTTP.
- Parse SSE or provider-specific streaming events.
- Accumulate partial tool calls.
- Map errors.
- List models.

### Target shape

For each protocol, use a small set of collaborators:

```text
OpenAIProtocolProvider
  OpenAIRequestBuilder
  OpenAIStreamParser
  OpenAIResponseParser
  OpenAIErrorMapper

AnthropicProtocolProvider
  AnthropicRequestBuilder
  AnthropicStreamParser
  AnthropicResponseParser
  AnthropicErrorMapper

GeminiProtocolProvider
  GeminiRequestBuilder
  GeminiStreamParser
  GeminiResponseParser
  GeminiErrorMapper
```

The provider remains the orchestrator. It should be easy to read:

1. Validate config.
2. Build request.
3. Send request.
4. Parse stream or full response.
5. Yield `LlmResponseChunk` or `LlmError`.

### Use typed DTOs at the edges

The current manual `JsonObject` construction is flexible, but it makes accidental shape changes easy. Prefer typed request/response records where the protocol is stable. Keep `JsonNode` only for genuinely open schema areas such as arbitrary tool schemas.

Example:

```csharp
internal sealed record OpenAIMessageDto(
    string Role,
    IReadOnlyList<OpenAIContentPartDto> Content);
```

This makes request builders easier to test and review.

### Make streaming parsers pure

Streaming parsers should accept provider event text and emit protocol-neutral chunks. They should not know about `HttpClient`.

Example:

```csharp
public IReadOnlyList<LlmResponseChunk> Feed(string sseDataLine);
```

Good parser tests:

- Tool-call argument deltas join correctly.
- Multiple tool calls in one assistant turn remain separate.
- Reasoning/thinking content is handled consistently.
- End-of-stream events produce no duplicate final chunks.
- Malformed events return a domain error instead of throwing out of the stream.

### Inject HTTP

`ProtocolProviderBase<TConfig>` currently owns a shared static `HttpClient`. Static reuse is good for sockets, but it makes tests and host-level settings harder.

Recommendation:

- Accept an `HttpClient` or `IHttpTransport` through provider construction.
- Keep a default shared instance for production.
- Let tests pass a fake handler.

## Phase 3: Extract Grasshopper State Machines

The hardest-to-maintain GH code is not hard because it is badly written. It is hard because Grasshopper solve lifecycle, signal latching, UI scheduling, and domain policy are interleaved.

### `StatefulComponentBase`

Current responsibilities:

- Component lifecycle state.
- Clear/reset behavior.
- Native boolean edge detection.
- Signal input observation.
- Consume-once signal bookkeeping.
- Sequence ordering.
- Foreign-source detection.
- Scheduled solve helpers.
- Runtime message integration.

Recommendation:

Extract:

- `SignalInputTracker`: observes volatile data, deduplicates by signal sequence/source, exposes ordered consumed signals.
- `SolveLifecycleState`: owns component state transitions and clear/reset behavior.
- `GhSolveScheduler`: wraps `ScheduleSolution` and callback timing.
- `NativeInputEdgeTracker`: handles boolean/native trigger inputs.

The base class should remain the adapter that wires those helpers into GH.

Good tests:

- A signal emitted twice on the same wire is consumed once.
- Signals from several inputs are consumed in sequence order.
- Clearing resets consumed state.
- Foreign-source detection does not block legitimate downstream signals.
- Scheduled solves do not emit stale latched data.

### `RoutingComponentBase<TData>`

Current responsibilities:

- Base-owned signal input.
- Success/fail output registration.
- Optional auxiliary output registration.
- Push/read/latch state machine.
- Retry scheduling.
- Runtime status and error handling.

Recommendation:

- Move the push/read/latch policy into a `RoutingRunState<TData>` class.
- Keep parameter registration in the GH base class.
- Move `RoutingResult` into its own file and make it the only way subclasses report outcome.
- Make retry decisions explicit and testable.

The goal is that subclasses read like:

```csharp
protected override RoutingResult<MyData> Run(MyInput input)
```

instead of participating in the base class lifecycle.

## Phase 4: Extract Pure Policies From GH Components

### `Recorder`

Current issue:

- Turn assembly policy lives directly in a GH component.
- Prompt, response, feedback, and tool signals are consumed by sequence order.
- Assistant tool calls and user tool results must be recorded in provider-valid order.

Recommendation:

- Extract `ConversationRecorder`.
- Inputs: current conversation plus ordered signal events.
- Output: new conversation, emitted signal, warnings.
- Keep GH data access and Goo conversion in `Recorder`.

Good tests:

- Prompt starts a user turn.
- Feedback merges into the last user turn when intended.
- Assistant response appends after user.
- Tool-call assistant turn is recorded before tool-result user turn.
- Instructions are carried forward only when expected.

### `Reasoner`

Current issue:

- Provider selection, model config, conversation read, tool definition collection, streaming buffer, cancellation, and signal emission are all in the component.

Recommendation:

- Extract `ReasonerRun`.
- Extract `InferenceRunner` that accepts `ILlmProvider`, `Conversation`, tools, config, and cancellation.
- Keep GH scheduling in the component.
- Normalize streaming output into a small immutable run state.

Good tests:

- Provider errors map to failure signals.
- Cancellation does not emit a stale success.
- Tool-call chunks produce tool-call signals.
- Text chunks produce response signals.
- Empty provider output is handled explicitly.

### `Router`

Current issue:

- Tool dispatch policy is embedded in a variable-parameter GH component.

Recommendation:

- Extract `ToolDispatchRound`.
- Inputs: tool calls, available output names, returning tool results.
- Outputs: dispatch signals, feedback signal, pending IDs, warnings.

Good tests:

- Multiple calls to the same tool are grouped into one dispatch signal.
- Unknown tool calls get synthetic error results.
- Results are forwarded only when every dispatched ID is answered.
- Out-of-order results produce one combined feedback signal.
- Duplicate result IDs are either ignored or produce a deterministic warning.

### `ToolComponentBase`

Current issue:

- Batch execution, async scheduling, cancellation, and GH output latching are mixed.

Recommendation:

- Extract `ToolBatchRunner`.
- Keep `ExecuteCall` and `ExecuteCallAsync` as subclass hooks.
- Centralize "one result block per call" enforcement.
- Make async cancellation behavior shared with Reasoner and Summarizer.

Good tests:

- Every dispatched call gets a `ToolResultContent`.
- Exceptions in one call do not drop the whole batch unless intended.
- Cancellation prevents late emission.
- Empty dispatch signal produces a warning and no new result.

### `Chatbox`

Current issue:

- Signal source behavior, active window ownership, harness proxy, persistence, emoji identity, and icon rendering are in one component.

Recommendation:

- Extract `ChatboxSignalSource`.
- Extract `ChatboxWindowRegistry`.
- Extract `ChatboxHarnessController`.
- Extract `ChatboxIdentity` or `EmojiIdentity`.

This will also make the panel and component less tightly coupled.

### `Composer`

Current issue:

- File loading, prompt/schema loading, catalog wiring, picker placement, menu actions, and GH placement are mixed.
- Some menu items are present but not implemented.

Recommendation:

- Extract file/prompt loading into `ComposerDefinitionStore`.
- Extract picker placement into `ComposerCanvasBuilder`.
- Either implement or remove "Save new .composer" and "Append to .composer" menu items until ready.
- Add validation around missing prompt/schema files and malformed composer definitions.

## Phase 5: Split `ChatWindow`

`src/Physalia.GH/Panels/ChatWindow.cs` is the largest file and the clearest maintainability bottleneck. It currently owns:

- Eto form creation and WebView hosting.
- WebView bridge routing.
- Windows-specific ownership/positioning.
- URI parsing.
- WebView2 message hookup.
- Prompt submission and image payload parsing.
- API-key setup and YAML creation.
- Provider availability probing.
- Polling the GH pipeline.
- Conversation-to-UI projection.
- Preset discovery and placement.
- Chatbox switching.
- Recorder placement and wiring.
- Clear-all behavior.
- Host script execution.

### Target classes

Recommended split:

```text
ChatWindow
  Owns Eto Form, WebView, timer, and lifecycle only.

ChatBridgeDispatcher
  Parses bridge commands and routes them to handlers.

ChatBridgeCommand
  Typed command model for submit/open/save-key/place-preset/etc.

ChatPayloadParser
  Parses text and image submit payloads.

UiStateProjector
  Converts Conversation, stream text, busy state, provider setup state, and chatbox list to UI DTOs.

ProviderSetupService
  Saves API keys, probes configured providers, and reports setup results.

PresetService
  Lists presets and reads descriptions.

PresetPlacementService
  Places bundled ghjson presets and wires anchors.

ChatboxRegistry
  Enumerates chatboxes and handles active chatbox switching.

RecorderPlacementService
  Places/wires a Recorder for the active Chatbox.

GhWindowHost
  Owns Windows-specific z-order/owner/position logic.

HostScriptExecutor
  Encapsulates JSON serialization and ExecuteScript calls.
```

`ChatWindow` should become small enough that it is clear which methods run on the UI thread and which methods call into GH.

### Bridge routing

Current issue:

- Commands are encoded as URI hosts and query strings.
- Large image messages use a second message path.
- C# and TypeScript mirror the contract manually.

Recommendation:

- Keep the current URI fallback for WebView compatibility, but route it into a typed command parser.
- Prefer one command envelope:

```json
{
  "type": "submit",
  "payload": { "text": "...", "images": [] }
}
```

- Generate or share DTO definitions if practical. At minimum, keep the C# UI records and TypeScript interfaces in parallel files with tests that compare sample payloads.

### Avoid `async void`

`HandleSubmit` and `HookWebMessage` currently use `async void`. Some event handlers must be `void`, but the async work should be delegated:

```csharp
private void OnSubmit(Uri uri)
{
    _ = HandleSubmitAsync(uri).ReportToRuntime(...);
}
```

This gives one place to log failures and avoids swallowed exceptions.

### Projection tests

`BuildMessages` and `BuildTool` are ideal pure functions. Move them to `UiStateProjector` and test:

- User messages with images.
- Assistant messages with tool calls.
- Tool results matched to tool calls.
- Tool errors.
- Feedback messages.
- Live stream appended to the active assistant group.

## Phase 6: Split `GhJsonBridge`

`src/Physalia.GH/Generation/GhJsonBridge.cs` is a useful facade, but it now contains several distinct policies:

- Export selected objects.
- Strip nicknames.
- Inject wireless feedback links.
- Inject picker values.
- Serialize Physalia schema to GhJSON.
- Validate JSON.
- Resolve component names against the catalog.
- Load and place GhJSON.
- Anchor preset placement to a live component.
- Rewire placeholders.
- Build `PutOptions`.
- Execute Put.
- Deselect placed objects.
- Reconcile variable parameters.
- Recreate missing connections.
- Restore feedback links.
- Restore picker values.

### Target split

Recommended classes:

```text
GhJsonBridge
  Thin facade used by components.

GhJsonExporter
  Export, metadata, nickname stripping.

GhJsonExtensions
  Feedback and Picker extension read/write.

GhJsonImporter
  Load/fix/place document.

GhJsonPlacementPlanner
  Computes offsets, anchors, placeholder rewires.

GhJsonVariableParameterRestorer
  Recreates custom variable params and dropped connections.

GhJsonComponentResolver
  Resolves names against ComponentCatalog.
```

### Testing strategy

The GH placement calls need Rhino/Grasshopper, but many policies do not:

- Extension payload serialization can be tested with in-memory GhJSON documents.
- Component name resolution can be tested with fixture catalogs.
- Placement offset calculation can be pure.
- Anchor rewire planning can be pure if it operates on DTOs before touching live GH objects.
- Invalid/missing metadata cases can be tested without the canvas.

Keep `GhJsonBridge` as the public internal facade so callers do not churn while internals are split.

## Phase 7: Improve The Svelte UI Structure

The UI is already componentized in several places, and `bridge.ts` is a good start. The main issue is that `App.svelte` still owns too much application state and bridge behavior.

### Recommended split

```text
src/lib/hostBridge.ts
  sendCommand, registerHostCallbacks, WebView2 fallback, URI fallback.

src/lib/state/chatStore.svelte.ts
  messages, stream, connected, busy, setup state.

src/lib/state/panelStore.svelte.ts
  setup page, preset page, manual definition page.

src/lib/chat/Header.svelte
  menu, clear, setup, preset/manual actions.

src/lib/chat/ChatView.svelte
  conversation rendering and composer.

src/lib/chat/ChatboxSwitcher.svelte
  bottom chatbox selector.
```

`App.svelte` should mostly compose these pieces.

### Stronger bridge contract

Current TypeScript types are useful, but outbound commands are still hand-built URLs in `App.svelte`.

Recommendation:

- Define typed outbound commands in `bridge.ts`.
- Add `sendHostCommand(command)` in a host bridge module.
- Keep URL encoding and WebView2 branching out of UI components.

Example:

```ts
type HostCommand =
  | { type: 'submit'; message: SubmitMessage }
  | { type: 'open'; url: string }
  | { type: 'saveKey'; provider: string; key: string }
  | { type: 'placePreset'; file: string };
```

### Tests

Start with pure TypeScript tests:

- `splitThinking`.
- `splitContent`.
- `stripDataUrl`.
- Host command serialization.
- Provider grouping in setup.

Then add lightweight browser tests only for high-risk bridge flows:

- Text submit.
- Image submit.
- Setup key submit.
- Preset placement command.

## Phase 8: Configuration, Secrets, And Model Catalogs

### API key configuration

The current setup does the right thing from a product perspective: keys are local, ignored by git, and not serialized into GH. The implementation should be made easier to reason about.

Recommendations:

- Centralize provider setup metadata used by C# and TypeScript.
- Keep provider IDs, config sections, and environment variables in one typed table.
- Test YAML read/write with comments, missing sections, duplicate keys, and unknown sections.
- Make write failures visible to the setup UI with specific messages.

Potential direction:

```csharp
public sealed record ApiKeyTarget(
    string ProviderId,
    string Section,
    string Leaf,
    string EnvironmentVariable,
    bool IsLlmProvider);
```

Use this table in both setup probing and provider resolution.

### Model catalogs

Recommendations:

- Separate model source fetching from model normalization.
- Cache with explicit expiration.
- Surface stale-cache state in UI or runtime messages where useful.
- Store sample upstream payloads as fixtures.
- Avoid making model list fetches block GH solves.

### Project build hygiene

`Physalia.GH.csproj` contains a prebuild target that references `scripts/update_model_info.py`. Verify whether that script exists and whether the target still belongs in the build. If the script is obsolete, remove the target. If it is required, move the script into a tracked location and make failure behavior explicit.

The UI build output is copied into `Files/UI/chat.html`. Keep generated output policy clear:

- Source lives in `Physalia.UI`.
- Generated `chat.html` is ignored or intentionally committed, but not ambiguous.
- `dotnet build` behavior should be documented for both local development and release packaging.

## Phase 9: Standardize Async, Cancellation, And Errors

Async work appears in:

- `Reasoner`.
- `Summarizer`.
- `ToolComponentBase`.
- model discovery components.
- provider clients.
- `ChatWindow`.
- Claude Code process/session management.

### Shared async runner for GH components

Create a small helper for the common pattern:

- Cancel any previous run.
- Start background work.
- Capture success/failure/cancellation.
- Schedule one GH solve to emit results.
- Prevent late emissions after cancellation or removal.

Potential shape:

```csharp
internal sealed class GhAsyncRun<TResult>
{
    public bool IsBusy { get; }
    public void Start(Func<CancellationToken, Task<TResult>> work);
    public bool TryTakeCompleted(out TResult result, out Exception? error);
    public void Cancel();
}
```

This reduces subtle differences across Reasoner, Summarizer, model info fetches, and tools.

### Error handling rules

Recommended policy:

- Pure domain code should throw only for programmer errors or invalid constructor arguments.
- Provider and infrastructure boundaries should convert expected failures to `LlmError` or domain result types.
- GH components should convert errors into runtime messages and failure signals.
- Broad `catch (Exception)` is acceptable at outer boundaries only, and should preserve context.

Concrete cleanup:

- Replace broad catches inside parsers with narrower catches where possible.
- Include provider name, model, endpoint, and status code in `LlmError` where applicable.
- Avoid swallowing exceptions silently except during best-effort UI teardown.

### Static mutable state

Current static state includes:

- `SignalSequencer`.
- Claude Code session pool and reaper.
- active chat window in `Chatbox`.
- model-list caches in model components.
- static `HttpClient`s.
- RhinoCommon index cache.

Some static state is reasonable, but each static cache needs:

- Clear owner.
- Thread-safety story.
- Reset path for tests.
- Disposal path if it owns resources.
- Comment explaining lifetime.

## Phase 10: Reduce Goo And Parameter Boilerplate

The `Goo` and `Parameters` folders contain a lot of repeated code. This is normal for Grasshopper plugins, but it can still be made clearer.

### Ephemeral Goo base

Many `GH_Goo` wrappers intentionally do not serialize data. Centralize that behavior.

Potential shape:

```csharp
public abstract class EphemeralGoo<T> : GH_Goo<T>
{
    public override bool Write(GH_IWriter writer) => true;
    public override bool Read(GH_IReader reader) => true;
}
```

Only use this if it matches every current no-op persistence case. Do not hide real persistence needs.

### Parameter metadata

Parameter classes repeat:

- Name.
- Nickname.
- Description.
- Type.
- GUID.
- Exposure.

Options:

- Keep explicit classes but use a shared base constructor.
- Use a small metadata record to reduce repeated assignments.
- Consider source generation only if parameter count keeps growing.

Avoid a complex reflection registry unless it removes real maintenance burden.

## Phase 11: Naming, Style, And Idioms

### Prefer explicit domain names

Good existing names include `Conversation`, `Instructions`, `ToolCallContent`, `ToolResultContent`, `ComponentCatalog`, and `CompactionResult`.

Continue that style. Avoid vague names such as:

- `data`.
- `item`.
- `state`.
- `result`.

Those names are fine in a five-line local scope. They become harmful in lifecycle and protocol code.

### Use records for value contracts

Use `record` or `readonly record struct` for:

- Bridge commands.
- Provider DTOs.
- Dispatch plans.
- Placement plans.
- Validation outcomes.
- Model catalog entries.

Use classes for:

- Objects with identity.
- Long-lived services.
- Stateful runners.
- UI/window adapters.

### Keep comments where they explain constraints

Many current comments explain important non-obvious constraints. Keep that style.

Good comments explain:

- GH solve-order hazards.
- Provider protocol requirements.
- Why a scheduled solve is needed.
- Why keys must not serialize.
- Why a fallback bridge exists.

Remove or avoid comments that only restate the next line of code.

### File organization

When a file passes a few hundred lines, ask whether it contains multiple policies. Split by responsibility, not by arbitrary line count.

Suggested rule of thumb:

- Domain model files can stay small and focused.
- Boundary classes can be longer, but only if they own one boundary.
- A class that owns UI, parsing, persistence, network, and GH graph actions should be split.

## High-Impact File Recommendations

| File | Recommendation |
| --- | --- |
| `src/Physalia.Core/Config/Api.cs` | Move file/env YAML access behind `IApiKeyStore`; test read/write fixtures. |
| `src/Physalia.Core/Config/LlmProviderFactory.cs` | Replace static singleton lookup with a provider registry that can accept dependencies. |
| `src/Physalia.Core/Models/ModelList.cs` | Split remote catalog clients from normalization and model value types. |
| `src/Physalia.Core/Providers/ProtocolProviderBase.cs` | Inject HTTP transport; move JSON schema conversion and model-list parsing into collaborators. |
| `src/Physalia.Core/Providers/Named/ClaudeCodeProvider.cs` | Move process/session mechanics out of Core; test transcript parsing separately. |
| `src/Physalia.Core/Providers/ClaudeCode/ClaudeCodeSession.cs` | Introduce process runner, temp-file owner, clock, and deterministic disposal. |
| `src/Physalia.Core/Web/WebTools.cs` | Move HTTP to adapter; keep formatting pure and fixture-tested. |
| `src/Physalia.Core/Tokens/AsyncMarkerTokenEstimator.cs` | Replace marker/throw pattern with separate sync and async estimator interfaces. |
| `src/Physalia.Core/Validation/JsonExtractor.cs` | Add fixture tests; keep heuristic behavior documented with examples. |
| `src/Physalia.GH/Components/StatefulComponentBase.cs` | Extract signal tracking, lifecycle state, edge detection, and scheduling helpers. |
| `src/Physalia.GH/Components/RoutingComponentBase.cs` | Extract push/read/latch state machine into a pure `RoutingRunState<TData>`. |
| `src/Physalia.GH/Components/Core/Recorder.cs` | Extract `ConversationRecorder` policy and test turn ordering. |
| `src/Physalia.GH/Components/Core/Reasoner.cs` | Extract `InferenceRunner` and async run state; keep GH code as adapter. |
| `src/Physalia.GH/Components/Regulators/Router.cs` | Extract `ToolDispatchRound`; test multi-tool and missing-tool behavior. |
| `src/Physalia.GH/Components/Tools/ToolComponentBase.cs` | Extract `ToolBatchRunner`; share async/cancellation pattern. |
| `src/Physalia.GH/Components/Core/Chatbox.cs` | Split signal source, window registry, harness controller, and identity/icon logic. |
| `src/Physalia.GH/Components/Core/Composer.cs` | Split file-definition loading from canvas placement; implement or remove unfinished menu items. |
| `src/Physalia.GH/Generation/GhJsonBridge.cs` | Keep facade, split exporter/importer/extensions/placement/variable-param restoration. |
| `src/Physalia.GH/Panels/ChatWindow.cs` | Split view shell, bridge dispatcher, state projector, provider setup, preset placement, and window host logic. |
| `src/Physalia.UI/src/App.svelte` | Move bridge sending and host callbacks into `hostBridge.ts`; split header/chat/switcher state. |
| `src/Physalia.UI/src/lib/bridge.ts` | Add outbound command types, not only host callback and data DTOs. |
| `src/Physalia.UI/src/lib/content.ts` | Add Vitest coverage; this is already nicely pure. |
| `src/Physalia.GH/Physalia.GH.csproj` | Verify the prebuild model-info script path; remove stale target or track the script. |

## Suggested Test Matrix

### Core unit tests

- Conversation append/merge.
- Instructions combination and propagation.
- Signal minting and sequence monotonicity.
- Compaction strategies and reassembly invariants.
- Token estimation helpers.
- JSON extraction and schema validation.
- Model ID normalization.
- Provider request builders.
- Provider streaming parsers.
- Web result formatting.
- Python output access inference.

### GH policy tests

These should test extracted policies without loading Rhino:

- Signal consumption order.
- Routing state transitions.
- Recorder turn assembly.
- Router dispatch/result aggregation.
- Tool batch result mapping.
- UI message projection.
- GhJSON extension serialization.
- Preset placement planning.

### Integration/manual tests

These still need Rhino/Grasshopper:

- Open chat window.
- Connect recorder.
- Send text-only prompt.
- Send image prompt.
- Stream assistant response.
- Tool call through Router to Web Search/Read URL.
- Multi-tool call.
- Clear all components.
- Place preset.
- Collapse/expand harness.
- Export/import ghjson with Feedback and Picker state.

### UI tests

- `splitThinking` and `splitContent`.
- Image token insertion/removal/renumbering.
- Host command serialization.
- Setup provider grouping.
- Basic browser smoke test for chat layout and composer submit.

## Proposed Implementation Order

1. Add test projects and a minimal CI/local test script.
2. Add tests for the already-pure Core helpers: conversation, compaction, validation, content parsing.
3. Extract and test provider request builders and streaming parsers.
4. Extract `ConversationRecorder` from `Recorder`.
5. Extract `ToolDispatchRound` from `Router`.
6. Extract `ToolBatchRunner` from `ToolComponentBase`.
7. Extract `SignalInputTracker` from `StatefulComponentBase`.
8. Split `ChatWindow` by introducing `UiStateProjector` and `ChatBridgeDispatcher` first.
9. Split `GhJsonBridge` behind its existing facade.
10. Move side-effectful Core implementations into provider/infrastructure adapters.
11. Clean up Goo/Parameter boilerplate once behavior is covered.
12. Tighten analyzers and style rules after the code has stable seams.

This order keeps the riskiest user-facing behavior protected before moving it.

## Anti-Goals

Avoid these:

- Do not rewrite all GH components at once.
- Do not replace the signal model with a generic event bus.
- Do not add dependency injection framework machinery unless simple constructors become insufficient.
- Do not hide Grasshopper lifecycle details behind vague abstractions.
- Do not move code into more files without reducing a real responsibility conflict.
- Do not remove detailed comments that explain GH scheduling or provider protocol constraints.
- Do not make API keys easier to serialize, log, or accidentally commit.

## Definition Of Excellent For This Codebase

Physalia will feel excellent to maintain when:

- Core domain behavior can be understood and tested without Rhino.
- Provider changes can be made with protocol fixtures instead of live API trial and error.
- GH components mostly adapt inputs/outputs to named domain services.
- Async components all follow one cancellation and emission pattern.
- The chat panel has a typed bridge and small focused services.
- GhJSON import/export behavior is covered by targeted tests.
- Build and UI generation behavior is predictable.
- New contributors can read `CLAUDE.md`, run tests, and find the right layer for a change.

The codebase has enough strong structure to get there incrementally. The important move is to make the implicit policies explicit, named, and tested.
