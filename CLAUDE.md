# Physalia — CLAUDE.md

## Role
Pair programmer. Give advice and answers by default. Make code changes **only** when explicitly asked ("make this change", "edit this", "fix this").

---

## Project Overview

Physalia is a Grasshopper (Rhino) AI plugin. It builds a visual node-based pipeline that connects LLM inference to Grasshopper document manipulation.

- **Working dir:** `C:\Users\rober\repos\Physalia\src`
- **Projects:** `Physalia.Core` (net7.0), `Physalia.GH` (net7.0-windows on Windows, net7.0 on Mac — OS-conditional TargetFrameworks)
- **Planning docs:** `planning/data-marshalling.md` (**authoritative** for signals + component lifecycle), `planning/physalia-primitives.md` (component spec), `planning/model-defaults.md` (**authoritative** for the known-model-defaults registry), `planning/incremental-building.md` (**authoritative** for staged generation: the plan block, the Build Plan tracker, why the digest owns the report's closing instruction), `planning/api_research.md`, `src/planning/ghjson-implementation.md`

---

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
        Named/       ← OpenAICompatibleConfig, AnthropicConfig, GeminiConfig, LlamaCppConfig
    Providers/       ← ILlmProvider, ProtocolProviderBase (HttpClient + shared request/stream helpers)
        OpenAiProtocol/, Anthropic/, Gemini/  ← protocol providers (per-provider wire-format parsing)
        Named/       ← OpenAICompatibleProvider, AnthropicProvider, GeminiProvider, LlamaCppProvider
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
    OpenAIProtocolProvider    → OpenAICompatibleProvider, LlamaCppProvider
    AnthropicProtocolProvider → AnthropicProvider
    GeminiProtocolProvider    → GeminiProvider
```
- `ProtocolProviderBase` owns HttpClient, `TryGetConfig<T>`, `SendStreamingRequestAsync`, `SendForStringAsync`, `ReadStreamLineAsync`, `ParseModelIdsFromDataArray`. **Wire-format/SSE parsing stays per-protocol provider** — do not merge it.
- `ModelConfig` hierarchy mirrors the provider hierarchy. DeepSeek/Ollama/OpenRouter/Groq etc. ride `OpenAICompatibleProvider` via base-URL swap, not separate classes.
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

### API Key Resolution (`Config/`)
1. Environment variable (convention wins)
2. YAML config file (`Files/API_KEY_CONFIG.YAML`)

Fail explicitly if neither source has the key. No third fallback. **API keys are never serialized into GH files** (`GH_ModelConfig.Write/Read` are intentional no-ops).

---

## Signals & Component Lifecycle (reworked 2026-06; authoritative doc: `planning/data-marshalling.md`)

Events between pipeline components travel as **`PhySignal`s** — immutable, sequence-numbered, **latched** (no momentary pulses, no pulse-reset solves). One wire per hop, never a parallel data wire: the signal carries the event AND its data. **Carrier discipline (do not erode):** a signal holds exactly `Payload` (text trace / feedback string), `ContentBlocks` (a richer-than-text user turn, e.g. inline images — the Prompter→Conversation Log hop), and `Instructions` (the full inference context — the Conversation Log→LLM Call hop, where the trigger IS the data: the Conversation Log mints a signal carrying Instructions, a compaction component re-emits one carrying compacted Instructions, the LLM Call reads `signal.Instructions`). **No other typed carrier fields** — arbitrary data stays on typed wires/inputs; every field added here turns the signal into a god-object. `GH_Signal` casts to Instructions/Conversation/text so a typed input can consume a signal without manual deconstruction.

- `SignalSequencer` issues process-wide monotonic sequences; **sequence order is causal order**. Receivers keep a per-input consumed high-water mark, so each signal is consumed **exactly once** — idle re-solves, recomputes, and coalesced schedules can never re-fire, reorder, duplicate, or drop events. Correctness is by identity, not timing.
- **Two-layer base classes** (`src/Physalia.GH/Components/`):
  - `StatefulComponentBase : PhyBase` — solve state machine (`Empty / Active / SolveSuccess / SolveFailure` + canvas caption), `ObserveSignalInputs` (call every solve, even while Active), `TryConsumeOldestSignal` / `ConsumeAllSignals` (global sequence order), `LatchSuccess/LatchFailure` (mint latched outgoing signal; `emitSignal:false` = quiet), and `ScheduleStateSolve` — the **single scheduling funnel**, wall-clock honest (re-arms when GH's one collapsing document schedule flushes early), safe from background threads.
  - `RoutingComponentBase<TData> : StatefulComponentBase` — the routing contract. Base-owned `Signal` input (list, optional, registered last); outputs `Success Signal`(0) / `Fail Signal`(1). Subclasses implement `TryGetData` (usually `signal.Payload`), `PushSolve` (side effects), `ReadSolve` (result), optionally `IsReadReady` (settle gate, bounded retries). Async components (LLM Call) set `AutoScheduleRead => false` and call `RequestReadPass()` from their completion callback.
- Signal inputs accept **only** signals. A bare bool (Button/Toggle) has no payload, so wiring one into a Signal input is a hard error — same as text, numbers, or geometry. `ObserveSignalInputs` detects a foreign source by inspecting the source goo directly (it keeps its original type after the failed cast), so a null/empty wire is tolerated and only a genuinely foreign source fails loudly. Manual runs go through ConstructSignal, whose dedicated native Boolean Trigger input (`ObserveButtonPress` — one mint per false→true press, nothing on load/paste) mints a payload-carrying signal. That is the one sanctioned place a Button drives the pipeline.
- **Nothing in the lifecycle persists** — state, signals, and consume-once bookkeeping are session-only; every component reopens Empty.
- Rules for new components: never gate on bool edges between Physalia components; never encode ordering in `ScheduleSolution` delays; observe signal inputs every solve; the signal carries the data (Payload / ContentBlocks / Instructions) — never a parallel data wire, and never add a new carrier field for arbitrary types.

---

## The Harness — the plug-in's base unit (`src/Physalia.GH/Harness/`)

A **Harness** (`HarnessComponent`) holds its own `GH_Document`. The user's canvas carries only the
proxy node; the entire Physalia pipeline — Chat included — lives inside it. Right-click →
**"Edit Harness"** points the canvas at the inner document, the canvas return widget comes back,
double-click opens the chat window on the Chat inside. It needs no cluster input/output hooks: a
pipeline never exchanges *dataflow* with the canvas, it only scans it and writes to it by side effect.

- **Every Physalia component must live in a harness.** `HarnessResidency` (hooked from
  `PhyBase.AddedToDocument`) removes a stray on the next idle pass and says why on the Rhino command
  line. Exempt: the proxy itself, anything already inside a harness, `GhJsonBridge.IsImporting`, and
  anything added to a document that is not the canvas document (that is how a file load is told apart
  from a user placement — existing files are left alone).
- **`OnPingDocument()` inside a harness returns the SUB-document.** Use `PhyDocuments.Host(this)` /
  `ActiveHost()` for anything meaning "the user's canvas" (grounding, placement, reports, memory
  scope); keep `ScheduleSolution`/`NewSolution` and co-resident peer lookups on the local document.
  The GhJSON library resolves its own target from the active canvas, so its writes are wrapped in
  `PhyDocuments.OnHostCanvas(...)` and its reads replaced by `GhJsonBridge.SerializeByGuids`.
- **Ownership is ours, not `GH_Document.Owner`** (a `ConditionalWeakTable` in `HarnessComponent`).
  Setting `Owner` makes Grasshopper paint its own cluster icon whose menu disposes the document.
- **Presets are stock `.gh` files** in `Files/PRESETS`, each one a harness's worth of pipeline —
  exactly what saving from inside a harness produces. Loading one adds a NEW harness holding it.
  The library is split three ways (`PresetLibrary`): **`Physalia/`** (shipped), **`User/`** (saved by
  the user), **`Community/`** (reserved, empty). Nothing outside those folders is listed. Wire values
  are library-relative (`User/mine.gh`) and resolved by MATCH against the enumerated library, never by
  composing a path. **Save Harness as Preset…** writes to `User/` — on the proxy's right-click menu and
  on the **Harness** widget pill (second in the top-left column inside a harness, under "Back to
  document"); it refuses a harness with no Chat, since the loader would reject it.
- **Reading a preset re-issues every instance id** (`DocumentIds.MutateAll`): an archive carries the ids
  it was saved with, so the same preset placed twice would otherwise put duplicate `InstanceGuid`s in one
  file. Wires and groups are Grasshopper's own problem; a guid held in one of OUR fields is not — any
  component storing another object's `InstanceGuid` must implement **`IGuidLinked.RemapLinks`** and
  replace **only** guids the map contains (a link may point outside the document, as PyTransmitter's
  does). A normal file load (`HarnessComponent.Read`) deliberately preserves ids.
- **A document may hold any number of harnesses** — one per line of work. Nothing is ever replaced or
  swept: each placement mints its own Chat (except the first, which adopts the window's detached one),
  drops the proxy at the first free spot right of the window (`PlaceHarness` steps down past anything
  already there), and switches the window to the new Chat. The switcher row is the way back.
- Nothing is placed automatically: the chat window's **Home** screen offers "Place predefined harness"
  and "Place empty harness", and the header menu carries the same two ("Add preset" / "Add empty
  harness") for once a conversation is under way.
- **Home** is the chat window's entry screen — a house icon leading the switcher row, always present,
  always divided off from the chat dots. It is a window state (`ChatWindow._home`), not a Chat. The
  canvas widget always opens on Home; double-clicking a harness opens on the first Chat inside it.

Detail: memory note `harness-subdocument`.

---

## GH Component Inventory

### Built (`src/Physalia.GH/Components/`)
| Folder | Components |
|---|---|
| **Pipeline** | Harness (the base unit — a proxy over its own sub-document holding the pipeline; right-click "Edit Harness" to go in, double-click opens the chat window), System Prompt (system prompt assembly; takes a `Grounding` list folded into the prompt), Chat (chat window entry point; mints Prompt Signals; displays the wired Conversation Log's conversation; lives INSIDE a harness. An ordinary node on the canvas — no double-click gesture, no tint of its own: the harness proxy is the only door onto the window), Conversation Log (append-only conversation log; identity-based turns via four Signal inputs — input order: System Prompt, Prompt Signal, Grounding, Human Tools, Response Signal, Feedback Signal, LLM Tool Signal), LLM Call (async LLM forward pass) |
| **Guardrails** | Schema Validator (JSON extraction + schema validation), GH Definition Validator (GhJSON/ghpatch parse + library schema + structural integrity), Component Resolver, Required Input Check (statically knowable wiring defects: required inputs wired/internalized, multi-wire into item-access inputs, endpoint paramIndex bounds, orphan data components — full graphs and ghpatch adds), Fidelity Check (post-placement intent-vs-realization diff via the authored-placement ledger; self-sources the definition recorded at placement when its Definition input is unwired/miswired; full graphs only, patches pass through), Runtime Health Check (was Canvas Observation — errors/dead/null scan with sampled values; Fail on Warnings is a context-MENU toggle, not an input — never register an input before the base-appended Signal on a shipped RoutingComponentBase subclass, it shifts saved-doc param layouts), Geometry Observation (viewport snapshot; single Signal output via `HasFailOutput => false`), Geometry Report (text-only spatial digest: per-component bboxes, disjoint groups + gaps, containments — the non-image fidelity feedback; single Signal output via `HasFailOutput => false`. Its closing instruction is single-shot — "matches your intent → reply in prose" — UNLESS the Message input carries a Build Plan progress digest, detected by `BuildPlanParser.DigestMarker`, in which case the digest's staged instruction replaces it and leads the report) |
| **GhPython** | PyTransmitter (pushes generated Python into a linked Script component — linked via its right-click "Link to Script Component" picker over the HOST canvas, since a grip drag cannot cross into a harness; the drag arrow itself is hosted by the harness proxy; routes its errors; when an enabled Interface Lock grip-links to it, pushes **code only** — never restructures the target's params — and rejects submissions declaring unknown input/output names with corrective Fail feedback), PythonShortcut |
| **Grounding** | ClusterGrounder (.ghx cluster — scaffold), PythonGrounder (python function — scaffold), CanvasStateGrounder, ComponentCatalogGrounder, DocumentUnitsGrounder, Tools Present (`ToolsInUse` — scans Router-wired tool nodes, emits `ToolsGrounding`; lives here, not under LLM Tools, because its output is grounding), Interface Lock (`InterfaceLock` — grip-links to a **PyTransmitter** via its own bottom arrow/gradient wire, reads the transmitter's target script component and emits `ScriptInterfaceGrounding`: the exact inputs (name/type-hint/access) and outputs (name/access) rendered as verbatim-copyable PythonComponent JSON entries, declared LOCKED; the same link makes the transmitter enforce the contract — see GhPython row. Refreshes via a SolutionEnd signature watch; disabling the component suspends the lock without unlinking) — all emit `GH_Grounding` for the Conversation Log's Grounding input. `Grounding` is a Core discriminated union (`ComponentCatalogGrounding` migrated from System Prompt's old catalog input; `GH_Grounding.CastFrom` adapts producer goo like `GH_ComponentCatalog`) |
| **LLM Tools** | Model-callable tools (`LlmToolComponentBase : StatefulComponentBase`; Core type `LlmToolDefinition`, goo `GH_LlmToolDefinition`, param `Param_LlmToolDefinition`): WebSearch, ReadUrl, MemoryTool, RhinoGeometryTool, ComponentSearch, RhinoCommonSearch — plus Router (dispatch loop). Tools Present lives in the **Grounding** section |
| **Human Tools** | Chat-window affordances for the HUMAN, not the model (`HumanToolComponentBase : PhyBase` — passive emitters: no inputs, one `Param_HumanTool` output; Core union `HumanTool` in `Physalia.Core/HumanTools/`): Geometry Snapshot + View Snapshot (both `SnapshotToolComponentBase`, which owns the shared "Send With Default Message" context-menu toggle: on = the capture is sent as its own message carrying an editable default message, off = it attaches to the prompt box for the human to caption, on its OWN image lane independent of Add Image and of the other snapshot tool. Geometry Snapshot frames the camera on transmitter-generated geometry and is armed only while such geometry exists; **View Snapshot captures the active viewport as-is — no geometry scan, no camera move, so wired is armed**), Add Image (enables image paste/drop/picker in the prompt box — image intake is fully disabled without it, except for a snapshot tool's own attach lane, see `ConversationLog.AcceptsPromptImages`), Export Conversation (header button → saves the viewed conversation as a .txt transcript; **replaced the `/export` slash command**, which no longer exists — the composer now has no built-in commands), Signal Trace (header button → opens `SignalTraceWindow`; **replaced the signal-trace canvas widget**, which was deleted. The trace log itself is still process-wide/session-wide, not per-conversation). Wired into the Conversation Log's Human Tools input; never touch the system prompt, never advertised to the model |
| **Models** | AnthropicModel/Tweaker, GeminiModel/Tweaker, OpenAICompatibleModel/Tweaker, ModelInformation, LlamaCppModelInfo, ApiKeys (+ `ModelComponentBase`, `TweakerComponentBase<TConfig>`) |
| **Control Flow** | Feedback, FeedbackCollector (wireless signal transport via grip-link; deliberately breaks the GH DAG), Detect JSON (presence gate — single Signal output via `HasFailOutput => false`; attempted JSON, even malformed, passes through; plain conversation dead-ends quietly inside the component via `RoutingResult.Fail(emitSignal: false)`), Build Plan (staged generation: parses the model's `<plan>` block out of each response and renders a progress digest on a `Progress` text output for the Geometry Report's Message input — a pass-through tap, never a gate; see `planning/incremental-building.md`), Signal Limiter (caps total loop rounds), Stall Guard (caps *identical* failure rounds — fingerprints failure payloads; escalates at the Stall Limit, suppresses re-emission beyond it; Stall Limit is input 0, single Success Signal output — parked loop = STALLED caption only, nothing emitted) |
| **Serializers** | Serializer / Deserializer (.ghjson canvas export/import via `GhJsonBridge`), SchemaTranslator |
| **Signals** | ConstructSignal (manual mint), DeconstructSignal (passive inspect — never consumes) |
| **Tokens** | TokenEstimator |
| **Utility** | Picker, Conversation/Message/Instructions Compositors + Decompositors |

### Planned, not yet built (spec: `planning/physalia-primitives.md`)
PyValidator, Receiver, Counter, Meter, Monitor, Aggregator — plus LLM Call alternate roles via `.skill` files (Distiller, Reflector, Interpreter, etc.).

---

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
- Copyright header: `Copyright (c) 2026 Physalia Contributors / SPDX-License-Identifier: AGPL-3.0-or-later`

## GH Rendering Patterns
- `GH_FontServer.StandardAdjusted`: zoom-aware text in custom `Render()`.
- Custom `Layout()` without `base.Layout()`: manually set all param `Attributes.Pivot` + `Bounds`.
- `GH_Capsule.AddOutputGrip(y)`: visual only — param bounds must be set separately for wire interaction.
- `ContextMenuStrip` (WinForms) works on GH canvas; `Eto.ContextMenu` does not.
- MidY: `Bounds.Y + Bounds.Height / 2f` (no `MidY` helper).
- `InstanceGuid` = per-object UUID. `ComponentGuid` = static type GUID. Always use `InstanceGuid` for serialization/lookup.
- Grip drag-to-link (Feedback, PyTransmitter): shared state machine in `Attributes/GripLinkAttrib.cs`.

## GH Async Pattern
- `AddRuntimeMessage` must be called during `SolveInstance` on the main thread.
- Pattern: store warning in a field from the async task → emit via `AddRuntimeMessage` in `SolveInstance` → clear field.
- Lifecycle components marshal async completion via `RequestReadPass()` / `ScheduleStateSolve` (safe from background threads) — never act on results directly from a `Task.Run` continuation.

## Build
- `System.Drawing` warnings (CA1416) are false positives — suppress with `<NoWarn>$(NoWarn);CA1416</NoWarn>`.
- StyleCop enforced; SA1101 suppressed (underscore prefix convention used).
- **MUST DO — after ANY change to the Svelte UI (`src/Physalia.UI`), build with `dotnet build src/Physalia.slnx -c Debug` (NOT `npm run build` alone) so the rebuilt UI is embedded into `Physalia.GH`.** `npm run build` only writes `src/Physalia.UI/dist/index.html`; the MSBuild `BuildPhysaliaUI` target refreshes that, and `Physalia.GH` (ProjectReference) embeds it as the `Physalia.GH.chat.html` resource via its `EmbedChatHtml` target. The UI bundle is **embedded in the assembly, not shipped loose in `Files/`** (`Files/` is reserved for user-alterable content); at runtime `ChatWindow.LoadUi` extracts it to `%TEMP%/Physalia/chat-<version>.html` and loads it via `file://`. Skipping the `dotnet build` strands the change in `dist/` and Rhino loads the old UI. (Build `-c Release` too when targeting the Release output.)

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
        /planning
            ghjson-implementation.md
    /planning
        data-marshalling.md      ← signals + lifecycle (authoritative)
        physalia-primitives.md   ← component spec
        api_research.md
    /Files
        API_KEY_CONFIG.YAML (+ .example)
        /SKILLS           ← .skill files (LLM Call instructions)
        /SYSTEM_PROMPTS
        /PROMPTS
        /RECEIVERS        ← .receiver files
        /PRESETS          ← preset harnesses (.gh — a saved harness sub-document)
            /Physalia     ← shipped with the plug-in
            /User         ← written by "Save Harness as Preset…"
            /Community    ← reserved, not populated yet
        /MEMORIES         ← memory tool: /GLOBAL and /LOCAL/<document-key>
        /agent_guides
```
