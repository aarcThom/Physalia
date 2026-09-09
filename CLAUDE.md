# Physalia — CLAUDE.md

## Role
Pair programmer. Give advice and answers by default. Make code changes **only** when explicitly asked ("make this change", "edit this", "fix this").

---

## Project Overview

Physalia is a Grasshopper (Rhino) AI plugin. It builds a visual node-based pipeline that connects LLM inference to Grasshopper document manipulation.

- **Working dir:** `C:\Users\rober\repos\Physalia\src`
- **Projects:** `Physalia.Core` (net7.0), `Physalia.GH` (net7.0-windows on Windows, net7.0 on Mac — OS-conditional TargetFrameworks), `Physalia.McpBridge` (**net8.0 console exe**, launched as a subprocess, never linked)

### Where the detail lives

**This file is a map plus the rules that apply to every session.** The reasoning behind each
subsystem — the forks taken, the traps found live, the invariants that look arbitrary until you know
why — was split out on 2026-09-08 to keep this file inside its size limit. **Read the matching doc
before changing that subsystem**; the summaries here are not sufficient to work from.

| Subsystem | Doc |
|---|---|
| Namespaces, conversation model, provider hierarchy, credentials | `planning/core-architecture.md` |
| Triggers, conditions/relays, delegation, Budget Guard, persistence, Pipeline State | `planning/triggers-conditions-delegation.md` |
| The Harness (sub-document, inlets/outlets, presets, panel) | `planning/harness.md` |
| HTTP APIs (API Call node, endpoint store, paging) | `planning/http-apis.md` |
| MCP client (bridge, stdio transport, config, OAuth) | `planning/mcp-client.md` |
| Project folders, `.phy` packages, harness names, harness panel, tool approval | `planning/project-files-and-phy.md` |
| Full component inventory — what every node is and why | `planning/component-inventory.md` |
| Provider integration (defaults registry, CLI providers, Codex tools) | `planning/provider-integration.md` |
| Publishing to Rhino's package manager (yak) | `planning/yak-publishing.md` |
| Original wording of the sections condensed here rather than moved | `planning/claude-md-condensed-sections.md` |

Older authoritative docs, unchanged: `planning/data-marshalling.md` (signals + component lifecycle),
`planning/physalia-primitives.md` (component spec), `planning/model-defaults.md` (known-model-defaults
registry), `planning/incremental-building.md` (staged generation), `planning/pdf-tools.md` (the Read
PDF pair), `planning/pre-ship-testing.md` (the last pass before a release — as of 2026-09-07 only F3
remains, driven by `tools/overnight/Watch-OvernightRun.ps1`), `planning/mac-port.md` (the deferred
macOS release), `planning/api_research.md`, `src/planning/ghjson-implementation.md`.

---

## Core Architecture

**Boundary rule:** `Physalia.Core` is a pure functional library — no side effects, no GH dependency.
GH owns all mutable state.

```
Physalia.Core/
    Common/          ← Result<T,E>, LlmError, LlmErrorKind, LlmResponseChunk, LlmUsage,
                       LlmToolCall, HttpErrorMapper, StringHelpers
    Config/          ← ModelApi, ProviderCatalog, ProviderActivation, ModelApiResolver
        Secrets/     ← ISecretStore, DpapiSecretStore, FileSecretStore, SecretStores.For
    ConvoInstruct/   ← Role, MessageContent, ImageSource, ConversationMessage,
                       Conversation, ConversationHelpers, Instructions
    Models/          ← ModelConfig (abstract), ModelEntry, ModelList
        Protocol/    ← OpenAIProtocolConfig, AnthropicProtocolConfig, GeminiProtocolConfig
        Named/       ← OpenAICompatibleConfig, AnthropicConfig, GeminiConfig
        Defaults/    ← the ONLY place a model name may be branched on
    Providers/       ← ILlmProvider, ProtocolProviderBase (HttpClient + shared request/stream helpers)
        OpenAiProtocol/, Anthropic/, Gemini/  ← per-provider wire-format parsing
        Named/       ← OpenAICompatibleProvider, AnthropicProvider, GeminiProvider
        ClaudeCode/, Codex/  ← local-CLI providers (warm process, no API key)
    Signals/         ← PhySignal, SignalOutcome, SignalSequencer, SignalAggregation
    Tokens/          ← ITokenEstimator + estimators, AsyncTokenEstimation, TokenEstimationHelpers
    Validation/      ← SchemaValidator, ValidationError, JsonExtractor
```

- **Conversation model:** `Role { User, Assistant }`; `MessageContent` union (`TextContent`,
  `ImageContent`, tool blocks); `ImageSource` union (`InlineImage`, `UrlImage`, `ManagedImage`);
  `ConversationMessage(Role, IReadOnlyList<MessageContent>)`; `Instructions(SystemPrompt, Conversation)`.
  `Conversation` is a **class** — `Append()` returns a new one and enforces no consecutive same-role
  turns; `MergeIntoLastUserMessage()` handles user text arriving when the last turn is already a user
  turn. **Images travel inside `ConversationMessage`, never as a side-channel.**
- **Provider hierarchy is abstract classes, not interfaces** (shared `HttpClient` state):
  `ProtocolProviderBase` → `OpenAIProtocolProvider` / `AnthropicProtocolProvider` /
  `GeminiProtocolProvider` → the named providers. The base owns HttpClient, `TryGetConfig<T>`,
  `SendStreamingRequestAsync`, `SendForStringAsync`, `ReadStreamLineAsync`, `ParseModelIdsFromDataArray`;
  **wire-format/SSE parsing stays per-protocol — do not merge it.** DeepSeek/Ollama/OpenRouter/Groq ride
  `OpenAICompatibleProvider` via a base-URL swap, not new classes. `HttpErrorMapper.MapStatusCode` is the
  single HTTP-status → `LlmErrorKind` source.
- `IAsyncEnumerable<Result<LlmResponseChunk, LlmError>> StreamAsync(Conversation, string systemPrompt, ModelConfig, CancellationToken)`.
- `Result<T,E>` is our own (`.Ok`/`.Err` nested records), no external dependency.
  `LlmError(LlmErrorKind Kind, string Message)`; `LlmErrorKind { Network, Auth, RateLimit, InvalidRequest, Timeout, Cancelled }`;
  `LlmResponseChunk(string? ContentDelta, bool IsLast, LlmUsage? Usage, IReadOnlyList<LlmToolCall>? ToolCalls = null)`.
- **Validation:** `JsonExtractor.ExtractJson/PrettyPrint` (strip prose / fences) and
  `SchemaValidator.Validate(json, schema) → Result<string, ValidationError>`, both pure.

### Credentials — the short version (full reasoning: `planning/core-architecture.md`)
A key and its endpoint are ONE fact (`ModelApi(Provider, BaseUrl, Key)`), authored **only** in the chat
window, stored DPAPI-encrypted at `%LOCALAPPDATA%/Physalia/credentials.dat`. There is **no file-based
fallback — do not reintroduce one** (`API_KEY_CONFIG.YAML`, its parser and importer are deleted).
- `ModelApiResolver` is the single read path, with exactly two sources in order: **environment variable**
  (names in `ProviderCatalog`), then the encrypted store. Its environment lookup is **injected**, or the
  order is untestable.
- **Availability is not consent.** `ProviderActivation` (`providers.json`, plain JSON on purpose) is the
  opt-in list; `Resolve` returns null for anything not on it, `StatusFor` is the un-gated view the setup
  page needs. Typing a key activates it; a merely *found* key must be added deliberately.
- **`ProviderCatalog` is the one vocabulary** — keep it in step with the UI's `providers.ts`.
- **`ISecretStore` is the ONLY platform seam** (macOS Keychain = one class + one line in `SecretStores.For`).
  DPAPI is our own ~40-line P/Invoke shared with the bridge by **linked compile**, so there is one
  implementation and zero new package references.
- `Unreadable` is not `Empty` — a store written by another Windows account must not be reported as "nothing
  configured", and saving over it is refused.
- **API keys are never serialized into GH files** (`GH_ModelApi`/`GH_ModelConfig` `Write`/`Read` are
  intentional no-ops). Reads are cached 3s + explicit invalidate.
- Editing takes a **blank key box to mean "keep the stored key"**; **Disconnect** is the separate forget
  verb, and every connected provider can be switched off, Claude Code and Codex included.

---

## Signals & Component Lifecycle (authoritative: `planning/data-marshalling.md`)

Events between components travel as **`PhySignal`s** — immutable, sequence-numbered, **latched** (no
momentary pulses). One wire per hop, never a parallel data wire: the signal carries the event AND its data.

- **Carrier discipline (do not erode):** a signal holds exactly `Payload` (text trace), `ContentBlocks`
  (a richer user turn, e.g. inline images) and `Instructions` (the full inference context, on the
  Conversation Log→LLM Call hop). **No other typed carrier fields** — every one added turns the signal
  into a god-object. `GH_Signal` casts to Instructions/Conversation/text.
- Separately it carries **provenance**: `SourceId`/`SourceName`/`Timestamp` + `Origins` (read via
  `OriginTrail`, never branched on), because aggregators re-mint under their own identity.
  **Every aggregator must use `SignalAggregation.Combine`** — a branch with no blocks of its own
  contributes its payload AS a `TextContent` block, or merged text is recorded nowhere and silently lost.
- `SignalSequencer` issues process-wide monotonic sequences; **sequence order is causal order**.
  Receivers keep a per-input consumed high-water mark, so each signal is consumed **exactly once** —
  correctness is by identity, not timing.
- **Two-layer base classes** (`src/Physalia.GH/Components/`):
  - `StatefulComponentBase : PhyBase` — solve state machine (`Empty/Active/SolveSuccess/SolveFailure` +
    caption), `ObserveSignalInputs` (every solve, even while Active), `TryConsumeOldestSignal` /
    `ConsumeAllSignals`, `LatchSuccess/LatchFailure` (`emitSignal:false` = quiet), and
    `ScheduleStateSolve` — the **single scheduling funnel**, wall-clock honest, safe from background threads.
  - `RoutingComponentBase<TData> : StatefulComponentBase` — base-owned `Signal` input (list, optional,
    registered **last**); outputs `Success Signal`(0) / `Fail Signal`(1). Subclasses implement
    `TryGetData` / `PushSolve` / `ReadSolve`, optionally `IsReadReady`. Async components set
    `AutoScheduleRead => false` and call `RequestReadPass()`.
  - Note the asymmetry: `LlmToolComponentBase` registers Signal **first**, so it may add inputs freely; on
    a shipped `RoutingComponentBase` subclass a new input shifts saved-doc param layouts — use a
    context-menu toggle instead.
- Signal inputs accept **only** signals; a bare bool has no payload and is a hard error. Manual runs go
  through Construct Signal's dedicated Boolean Trigger input.
- **Nothing in the lifecycle persists** — state, signals and consume-once marks are session-only; every
  component reopens Empty.
- Rules for new components: never gate on bool edges between Physalia components; never encode ordering in
  `ScheduleSolution` delays; observe signal inputs every solve; the signal carries the data.

---

## Subsystem summaries

Each is a paragraph; the doc named beside it is what you read before changing anything.

### The Harness — the plug-in's base unit (`planning/harness.md`)
A `HarnessComponent` holds its own `GH_Document`; the user's canvas carries only the proxy, and the whole
pipeline (Chat included) lives inside. **Dataflow crosses INWARD only** — a **Harness In** inside grows a
real input param on the proxy's left edge; what a pipeline *produces* is a side effect carried by the
proxy's drag arrows (outlets, one per transmitter inside). Placing Physalia components straight on the
canvas is legal. **`OnPingDocument()` inside a harness returns the SUB-document** — use
`PhyDocuments.Host(this)`/`ActiveHost()` for anything meaning "the user's canvas" (grounding, placement,
reports, memory scope) and keep `ScheduleSolution`/`NewSolution` local. Inlets bind by `InstanceGuid`
**never by position** (a param is a real object other wires point at); outlets, being arrows we paint, may
be rebuilt freely. Presets are stock `.gh` files under `PRESETS` (ours in the package, the user's in
the data folder — `PresetLibrary.DirectoryFor`); reading one re-issues every instance
id (`DocumentIds.MutateAll`), so any component storing another object's guid must implement
`IGuidLinked.RemapLinks`.

### Triggers, conditions, delegation (`planning/triggers-conditions-delegation.md`)
`SignalSourceBase<TEvent>` is the event tier (Timer, Folder Watcher, Rhino Changed, Data Changed, Watch
Modelling). **Arming is session-only and NEVER serialized** — a file that opened armed would spend money on
whatever machine opened it; the harness panel's "Disarm N triggers" is the kill switch (`TriggerRegistry`),
and Trigger Control puts the live list in the chat window, addressed by `InstanceGuid` never by nickname.
Bursts coalesce on a restarting settle timer; every wake-up goes through `PipelineWake.Ready` (re-asserts a
harness sub-document's `Enabled`, and never touches the solver lock on a user's file);
`PipelineFileWrites`/`PipelineRhinoWrites` keep the pipeline's own writes out of the triggers and
recordings. `SignalRelayBase` is the condition tier (Signal Gate, Hold Signal, Signal Switch, Signal
Throttle, plus For Each) and **forwards the ORIGINAL signal, never a re-mint** — a re-mint would strip the
Instructions a gate on that hop exists to pass through. Delegate/Task In/Task Out make a harness a tool of
another harness, paired on the inner document by `DelegationBroker`, one task at a time. **A pipeline with
any trigger armed and no Budget Guard has no upper bound on its bill** (`SpendPolicy` + `SpendLedger`,
checked before a call, no wire between them).

### HTTP APIs (`planning/http-apis.md`)
The **API Call** node lets the model read a configured HTTP API. **The model supplies a path and a query,
never a URL and never a header** — `ApiRequest.ComposeUri` enforces it, GET only. The tool walks paging
itself and delivers **one item per RECORD** on the Response output, while only an `ApiResponseSummary`
(counts, field names, one sample, an explicit partial-result marker) goes back to the model. Endpoints live
in `%LOCALAPPDATA%/Physalia/api-endpoints.json`; the key rides in the shared credential store. The catalog
is typed on the node's own `Description` input, so it **ships inside a preset**, and rides in the PROMPT via
`GroundingDirective`. A node with no endpoint picked advertises **nothing**. Store-backed nodes (this and
`McpServer`) reload on the file's `RevisionStamp`, not on an empty cache.

### MCP — Physalia is a CLIENT (`planning/mcp-client.md`)
**The official C# SDK cannot run inside Rhino** — measured; Rhino serves `System.Text.Json` from the shared
framework, so no packaging change fixes it. Hence: `Physalia.Core/Mcp/` implements **stdio only**
(`McpSession` + pooled `McpConnections`), and `Physalia.McpBridge` (net8.0 exe) relays remote/OAuth servers
verbatim — **stdout is the protocol, every diagnostic to stderr**. `McpServer` is the ONE node advertising
many tools (`Definitions` plural), names namespaced `{server}__{tool}`. **`Router.InspectConnection` must
read `LlmToolComponentBase.AdvertisedDefinitions`, never the Tool output's `VolatileData`** — a
signal-driven dispatch expires the Router, not the tool node, so VolatileData is empty. General lesson: any
component reading a PEER's output within a signal-driven solve must ask the component, not the solver.
Servers live in `%LOCALAPPDATA%/Physalia/mcp-servers.json` (the standard `mcpServers` block; the YAML is
gone). `Read()` expands `${VAR}`, `ReadRaw()` does not — **the setup page must use `ReadRaw`** or it bakes
resolved secrets into the file. On Windows, `McpExecutable.Resolve` is load-bearing: `npx` is a `.cmd` shim
and `CreateProcess` does not apply PATHEXT.

### Project files and `.phy` (`planning/project-files-and-phy.md`)
A harness is named `curious-cake-soap-fun` — four words **derived** from its `InstanceGuid`, never randomised
or stored — and owns `PROJECT_FILES/<name>/` in the data folder. `ProjectFolderInput` is the single resolver every node
calls (blank = the harness's own; no separator = a name under `PROJECT_FILES`; a separator = relative to the
saved `.gh`; rooted = verbatim). A rename MOVES the folder, and `_folderKey` is stored WITH its owning guid
so a paste cannot steal the original's downloads. A `.phy` is an ordinary zip (`manifest.json` +
`harness.gh` + `files/`) whose inner document is byte-identical to a preset `.gh`; format is decided by
CONTENT (`PK`), a future version is REFUSED, `ZipSafety` is the only extraction path, and re-fetchable
downloads are carried as ledger knowledge rather than bytes (a `.phy` saved to a destination the user picks
carries them whole). Nothing per-machine goes in. Tool approval (`ToolApprovalBroker` → a card in the chat
window) **fails closed on every edge**, including no window open — and `ChatWindow.CanAskUser`, not
`ActiveWindow is null`, is what keeps that honest now the window HIDES with Grasshopper.

---

## Settings live on the component they configure

Anything the user *sets* is stored and serialized **on the component that owns that thing**, never on the
Conversation Log — because a setting is only useful if it travels: into another harness, into the `.gh`,
and **inside a preset**.

| Setting | Owner |
|---|---|
| Catalog tab/panel selection, expose-signatures, include-legacy | `ComponentCatalogGrounder` |
| Cluster selection | `ClusterGrounder` |
| Units override (never changes the document) | `DocumentUnitsGrounder` |
| Advertise-to-the-model switch | **each `LlmToolComponentBase` node** |
| Send-with-default-message, snapshot wording | `SnapshotToolComponentBase` |
| Fail-on-warnings, pruner toggles, Picker value, Script I/O link, image paths | their own components |

- **The Conversation Log is a FAÇADE, not a store.** `RefreshSettingOwners` resolves the components feeding
  its Grounding and Human Tools inputs every solve (walking *through* bare relay params, so a tidy-up param
  cannot hide the owner); getters read the owners (**last one wins**), setters write **all** of them.
- **The tools "selection" is derived, not stored** — each node carries its own switch, so two nodes
  advertising the same tool name no longer share one checkbox. A parked tool must stay listed
  (`ScannedTools`) or there is nothing to switch back on.
- **null-vs-empty is load-bearing** (null = never configured, empty = include nothing) and GH's archive has
  no null — `SettingArchive` writes a `<key>Set` flag plus the value.
- Older files migrate once via `ConversationLog._legacy*` + `ApplyLegacySettings`, through
  `ScheduleStateSolve` (a raw `ScheduleSolution` would race the latch). The shipped presets are that case.

---

## GH Component Inventory — 109 components

Names only; **what each one is and why is in `planning/component-inventory.md`.** Ribbon section and code
folder are 1:1 apart from spelling; every folder is under `src/Physalia.GH/Components/` except the Harness
proxy (`src/Physalia.GH/Harness/`).

| Section (ribbon) | Components |
|---|---|
| **Pipeline** | Harness, System Prompt, Project Prompts, Chat, Conversation Log, LLM Call |
| **Guardrails** | Schema Validator, GH Definition Validator, Component Resolver, Required Input Check, Fidelity Check, Runtime Health Check, Geometry Observation, Geometry Report |
| **Grounding** | Cluster, Python, Canvas State, Component Catalog, Document Units, Rhino Document, Image Sources, Tools Present, Project Folder, Set Script I/O |
| **LLM Tools** | Router, WebSearch, ReadUrl, Memory, Create/Ref. Rhino Geometry, Drive Rhino (`run_rhino_script`), ComponentSearch, RhinoCommonSearch, Take Snapshot, Move In Space, Read PDF, MCP Server, API Call, Download File, Read File, Declare, Ask Human, Delegate, Pipeline State |
| **Human Tools** | Geometry Snapshot, View Snapshot, Add Image, Export Conversation, Signal Trace, Image Mark Up, Token Count, Trigger Control, Read PDF (`AddPdf`) |
| **Models** | Anthropic / Gemini / OpenAICompatible Model + Tweaker, ModelInformation, LlamaCppModelInfo, LlamaCpp API, Model API, ClaudeCodeModel, CodexModel |
| **Control Flow** | Feedback, Feedback Collector, Detect JSON, Build Plan, Signal Limiter, Merge Signal, Stall Guard, Signal Gate, Hold Signal, Signal Switch, Signal Throttle, For Each, Budget Guard |
| **Triggers** | Timer, Folder Watcher, Rhino Changed, Data Changed, Watch Modelling |
| **Signals** | Construct Signal, Construct Tool Call, Deconstruct Signal, Conversation/Message/Instructions Compositors + Decompositors |
| **I/O** | Harness In, Harness Out, Task In, Task Out |
| **Transmitters** | Component Transmitter (`CompTx`), C# Transmitter (`CsTx`), PyTransmitter |
| **Extra** | Serializer / Deserializer, Picker, Zoom Guid |

Planned, not yet built (`planning/physalia-primitives.md`): PyValidator, Counter, Meter, Monitor,
Aggregator, plus LLM Call alternate roles via `.skill` files. That doc's "Receiver" is a **different,
superseded** thing — the harness inlet is **Harness In**.

---

## Provider Integration — the rules (detail: `planning/provider-integration.md`)

- **Never branch on a model name outside `Physalia.Core/Models/Defaults/`.** Per-model quirks (thinking
  forms, sampling rejection, token-limit key names) live only in the ordered pattern tables there. Nullable
  config thinking fields carry user intent (`null` = auto → registry default; explicit Tweaker values win,
  mapped to the form the model accepts); a rejected thinking/sampling field is a table bug, not user error.
  Unknown models get the conservative fallback. Design guidelines: `planning/model-defaults.md` — read
  before touching.
- **Temperature:** Anthropic `0.0–1.0` (clamp on intake), OpenAI/Gemini/DeepSeek `0.0–2.0`. Newest Anthropic
  generations and OpenAI reasoning models **reject** non-default sampling; the latter need
  `max_completion_tokens`. `max_tokens` is **required** on Anthropic — always inject a default.
- **Provider-as-adapter:** DeepSeek / Ollama / OpenRouter / Groq = `OpenAICompatibleProvider` + base-URL swap.
- **Thinking rides inline as `<think>…</think>`** in streamed text, stripped from resent history by
  `ThinkingTags`; the registry shapes **requests only** — response parsing stays uniform per protocol.
- **Images:** OpenAI + Anthropic take arbitrary public URLs; Gemini needs GCS or the Files API.
  `ImageSource` is the union each adapter maps.
- **Local-CLI providers (Claude Code, Codex)** drive a CLI the user already signed into — no key stored or
  sent. One warm process per LLM Call pooled on `ModelConfig.SessionKey`; **seed then delta**, and a seed is
  text PLUS its images (`ConversationHelpers.ToSeedContent`) or snapshots go invisible on every reseed.
  They are plain text generators: the CLI's own tools off, empty temp workspace, Physalia's system prompt
  replaces the agent's. **Claude Code ignores `tools` entirely** — it can never drive a Router. **Codex
  advertises them** as `dynamicTools` and hands a call back *deferred* on the final chunk, so the canvas is
  wired exactly as for the HTTP providers; everything the model says after a tool call is dropped.

---

## C# Conventions

- Abstract base class over interface when all implementations are controlled and share state.
- `private readonly` fields + constructor injection; `ArgumentNullException.ThrowIfNull()` for null guard (net7.0).
- `ThrowIfNullOrWhiteSpace` is net8.0+ — use `string.IsNullOrWhiteSpace + throw ArgumentException` on net7.0.
- Template method: public non-virtual validates → calls `protected abstract` Core method.
- `HttpClient` as `protected readonly` on base class — never instantiate per-request.
- `throw new InvalidOperationException()` over base `Exception` for deserialization failures.
- Abstract properties for per-subclass constants (`ProviderName`, `MaxTokens`).
- XML doc: always multi-line, one tag per line, plain text in `<returns>` and `<param>`.
  `GenerateDocumentationFile` is on for all three projects, so a bad doc comment is a build error.
- Copyright header: `Copyright (c) 2026 Physalia Contributors / SPDX-License-Identifier: AGPL-3.0-or-later`

## GH Rendering Patterns
- `GH_FontServer.StandardAdjusted`: zoom-aware text in custom `Render()`.
- Custom `Layout()` without `base.Layout()`: manually set all param `Attributes.Pivot` + `Bounds`.
- `GH_Capsule.AddOutputGrip(y)`: visual only — param bounds must be set separately for wire interaction.
- **Compose one render channel by hand and you skip ALL of `base.Render`** — every non-Objects channel must
  fall through, or GH's own drawing (the wires arriving at your inputs) silently disappears.
- **`GH_DocumentObject`'s `NickName` setter raises nothing** (verified against the shipped assembly) and a
  MOVE raises nothing either; `ExpireLayout` is not a promise that `Layout()` runs. Hook a rename by
  overriding the virtual `NickName` setter, and make the sync two-way if the name is editable at both ends.
- `ContextMenuStrip` (WinForms) works on GH canvas; `Eto.ContextMenu` does not.
- MidY: `Bounds.Y + Bounds.Height / 2f` (no `MidY` helper).
- `InstanceGuid` = per-object UUID. `ComponentGuid` = static type GUID. Always use `InstanceGuid` for serialization/lookup.
- Grip drag-to-link (Feedback, PyTransmitter): shared state machine in `Attributes/GripLinkAttrib.cs`.
- **Measure, never hard-code a pixel size** in a WinForms panel or a custom capsule — a single-line `TextBox`
  ignores an assigned Height, and any DPI but 100% breaks constants. Re-measure on `OnHandleCreated`,
  `OnFontChanged` and `OnDpiChangedAfterParent`.

## GH Async Pattern
- `AddRuntimeMessage` must be called during `SolveInstance` on the main thread.
- Pattern: store warning in a field from the async task → emit via `AddRuntimeMessage` in `SolveInstance` → clear field.
- Lifecycle components marshal async completion via `RequestReadPass()` / `ScheduleStateSolve` (safe from background threads) — never act on results directly from a `Task.Run` continuation.
- **A harness-resident component reacting to a HOST-side event must `ExpireSolution(false)` and must NOT post
  a `ScheduleSolution`** — the host solves, the harness does not, and a disabled sub-document silently drops
  scheduled callbacks. Marking dirty is enough for anything upstream of the Conversation Log.

## Build
- `System.Drawing` warnings (CA1416) are false positives — suppress with `<NoWarn>$(NoWarn);CA1416</NoWarn>`.
- StyleCop enforced; SA1101 suppressed (underscore prefix convention used).
- **MUST DO — after ANY change to the Svelte UI (`src/Physalia.UI`), build with `dotnet build src/Physalia.slnx -c Debug` (NOT `npm run build` alone) so the rebuilt UI is embedded into `Physalia.GH`.** `npm run build` only writes `src/Physalia.UI/dist/index.html`; the MSBuild `BuildPhysaliaUI` target refreshes that, and `Physalia.GH` (ProjectReference) embeds it as the `Physalia.GH.chat.html` resource via its `EmbedChatHtml` target. The UI bundle is **embedded in the assembly, not shipped loose in `Files/`**; at runtime `ChatWindow.LoadUi` extracts it to `%TEMP%/Physalia/chat-<version>.html` and loads it via `file://`. Skipping the `dotnet build` strands the change in `dist/` and Rhino loads the old UI. (Build `-c Release` too when targeting the Release output.)

### Compatibility note — the `.gha` is no longer fully self-contained (2026-08-25)
PDF page rendering broke the single-file property, deliberately. **The merge rule is intact** — everything
ILRepack merges is still pure managed IL — but the shipped artifact is the `.gha` **plus native binaries**,
and packaging must carry them.
- `PdfPig` (Core) is merged normally: Apache-2.0, pure managed, no special handling.
- **`PDFtoImage` + `SkiaSharp` (GH) are DENYLISTED from `RepackGha`** — they are P/Invoke shims, and a
  renamed internalized shim inside a `.gha` loaded by `Assembly.LoadFrom` makes native resolution
  unpredictable. (A different reason from the JSON stack's denylist, which is about type identity.) The
  natives escape only because the `RepackInputDll` glob is non-recursive.
- **`PdfNativeLibrary` pins the lookup** with `NativeLibrary.SetDllImportResolver`, installed lazily on
  first render: a GH plug-in gets no `AssemblyDependencyResolver` and no host `.deps.json` probing, so the
  default search finds Rhino's directory and not ours. Without it the symptom is a `DllNotFoundException`
  **in Rhino only**, from a build that is healthy on the command line. It tries
  `runtimes/<os>-<arch>/native/`, then `runtimes/<os>/native/`, then a flattened copy beside the assembly.
- Covered: win-x64/arm64/x86, osx-x64/arm64, linux-x64/arm64. Elsewhere rendering reports itself unavailable
  **while text extraction keeps working**. `TrimNativePdbs` deletes the native `.pdb`s (`libSkiaSharp.pdb`
  alone is ~89 MB).
- **Verify in Rhino, not on the command line** — place a Read PDF tool and render a page.

## SystemPrompt Type Hints
Primitives: `Number`, `Integer`, `Boolean`, `Text`
Geometry: `Point`, `Vector`, `Plane`, `Line`, `Circle`, `Arc`, `Curve`, `Surface`, `Brep`, `Mesh`, `Geometry`, `Box`, `Transform`, `Interval`
Other: `Colour`

---

## File Layout
```
/Physalia
    /src
        /Physalia.Core
        /Physalia.GH
        /Physalia.McpBridge
        /Physalia.UI               ← Svelte chat UI, EMBEDDED into Physalia.GH at build
        /planning
            ghjson-implementation.md
    /planning                      ← the subsystem docs this file maps to, plus the older
                                     authoritative specs (data-marshalling, primitives, …)
    /Files                       ← SHIPPED runtime content, read-only in practice: a package update
                                   replaces this whole directory. Nothing the user or the pipeline
                                   WRITES may live here — see the data folder below.
        CHANGELOG.md      ← read by the update notice; one `## <version>` per release, newest first
        /SYSTEM_PROMPTS   ← /PREAMBLE + /SCHEMA, resolved by name from the System Prompt component
        /CLUSTERS         ← .ghcluster files + clusters.json manifest (Cluster Grounding)
        /PRESETS/Physalia ← preset harnesses shipped with the plug-in (.phy — a zip of manifest +
                             harness.gh + files/; plain .gh still read)
            /AI           ← the model-written set: a FOURTH library folder despite being nested here
                             (Enumerate is non-recursive, which is what keeps it out of the main
                             listing). Shown behind the chat window's pink Experimental toggle, so it
                             ships without being advertised — see planning/project-files-and-phy.md
        /MEMORIES, /PROJECT_FILES, /PRESETS/{User,Community}
                          ← EMPTY, and kept only to explain where they went (a README in each)
```

**The data folder — `%LOCALAPPDATA%/Physalia` (`~/.local/share/Physalia` elsewhere).** Everything the
user or the pipeline writes, because **Rhino 8 updates package-manager plug-ins silently at startup
and installs each version in a directory of its own**: anything beside the assembly is one update
away from being stranded, and `CopyLibraryFiles` wipes `$(TargetDir)Files` on every developer build
as well. `Physalia.Core/Config/PhyData` is the ONE place the user-or-package decision is made; never
compose `Files/<X>` beside the assembly again.
```
    %LOCALAPPDATA%/Physalia/
        credentials.dat   ← DPAPI-encrypted keys      } written only by the chat window's
        providers.json    ← the opt-in provider list  } setup pages
        mcp-servers.json  ← MCP servers               }
        api-endpoints.json← HTTP APIs                 }
        install.json      ← which build ran here last, for the silent-update notice (InstallStamp)
        /MEMORIES         ← memory tool: /GLOBAL and /LOCAL/<Memory Folder input, or the node's id>
        /PROJECT_FILES    ← one folder per harness, named after it: downloads, site data, /PDF,
                             conversation.json + /conversation-images (autosaved transcript),
                             runs.jsonl (one line per inference call)
                             (Files/PDFS is GONE — PDFs are project material, inside the project)
        /PRESETS/User     ← written by "Save Harness as Preset…"
        /PRESETS/Community← reserved, not populated yet
        /SYSTEM_PROMPTS   ← optional; OVERLAYS the shipped set, file by file
        /CLUSTERS         ← optional; OVERLAYS the shipped set, file by file
```
- **Overlay, not copy.** Where both roots hold a folder the user's is searched FIRST and shadows the
  shipped file of the same name, so their edits survive an update AND a fix to a shipped preamble
  still reaches them. `DataMigration` carries older builds' leftovers once, per entry, never merging.
- **The version is load-bearing in three places** (`Physalia.GH.csproj` `<Version>`): auto-update
  compares it, `Physalia_GHInfo` reports it, and a CHANGE between runs raises the update notice. Keep
  it plain semver — a pre-release tag opts every user out of automatic updates — and add a
  `## <version>` section to `Files/CHANGELOG.md` when bumping it, or a test fails.
- Full reasoning, and the five failures each rule prevents: `planning/project-files-and-phy.md`.
