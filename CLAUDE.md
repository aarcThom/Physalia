# Physalia — CLAUDE.md

## Role
Pair programmer. Give advice and answers by default. Make code changes **only** when explicitly asked ("make this change", "edit this", "fix this").

---

## Project Overview

Physalia is a Grasshopper (Rhino) AI plugin. It builds a visual node-based pipeline that connects LLM inference to Grasshopper document manipulation.

- **Working dir:** `C:\Users\rober\repos\Physalia\src`
- **Projects:** `Physalia.Core` (net7.0), `Physalia.GH` (net7.0-windows on Windows, net7.0 on Mac — OS-conditional TargetFrameworks), `Physalia.McpBridge` (**net8.0 console exe**, launched as a subprocess, never linked — see MCP below)
- **Planning docs:** `planning/data-marshalling.md` (**authoritative** for signals + component lifecycle), `planning/physalia-primitives.md` (component spec), `planning/model-defaults.md` (**authoritative** for the known-model-defaults registry), `planning/incremental-building.md` (**authoritative** for staged generation: the plan block, the Build Plan tracker, why the digest owns the report's closing instruction), `planning/pdf-tools.md` (**authoritative** for the Read PDF pair: the session registry, the zoom loop, the descriptor), `planning/api_research.md`, `src/planning/ghjson-implementation.md`

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

Events between pipeline components travel as **`PhySignal`s** — immutable, sequence-numbered, **latched** (no momentary pulses, no pulse-reset solves). One wire per hop, never a parallel data wire: the signal carries the event AND its data. **Carrier discipline (do not erode):** a signal holds exactly `Payload` (text trace / feedback string), `ContentBlocks` (a richer-than-text user turn, e.g. inline images — the Prompter→Conversation Log hop), and `Instructions` (the full inference context — the Conversation Log→LLM Call hop, where the trigger IS the data: the Conversation Log mints a signal carrying Instructions, a compaction component re-emits one carrying compacted Instructions, the LLM Call reads `signal.Instructions`). **No other typed carrier fields** — arbitrary data stays on typed wires/inputs; every field added here turns the signal into a god-object. `GH_Signal` casts to Instructions/Conversation/text so a typed input can consume a signal without manual deconstruction. Separate from the carriers, a signal also holds **provenance**: `SourceId`/`SourceName`/`Timestamp` plus `Origins` — the trail of components an event ultimately came from, read via `OriginTrail` (never branched on). It exists because an aggregator (Merge Signal, Feedback Collector) or an escalating pass-through (Stall Guard) re-mints under its OWN identity, which would otherwise erase the component that produced the text; `SignalAggregation.Combine` returns the combined trail and `LatchSuccess(origins:)` carries it. `ConversationLogBuilder` stamps it onto the recorded turn as `ConversationMessage.Sources`, which is how the chat window badges a feedback turn with the producing node's nickname and icon.

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
double-click opens the chat window on the Chat inside. **Dataflow crosses INWARD only**, and the
asymmetry is load-bearing: what a pipeline *produces* is an edit to the canvas (placement, a pushed
script), and GH has no mechanism for "a wire that writes" — so outputs are side effects carried by the
proxy's drag arrows (outlets). What a pipeline *consumes* is data the canvas already computed, and GH
hands us wires pointing inward for free — so a **Harness In** inside grows a real input param on the
proxy's LEFT edge (inlets). See the I/O row below.

- **A harness is where a pipeline belongs, not where it is forced to be.** Placing a Physalia
  component straight onto the user's canvas is legal (the `HarnessResidency` guard, which used to
  delete strays on the next idle pass, is **deleted** — 2026-08-17). Nothing needs repairing for
  that case: `PhyDocuments.Host()` on a canvas-resident component returns the canvas itself,
  `PhyDocuments.Harness()` is nullable everywhere it is consumed, `MasterGroupName(null)` falls back
  to the unsuffixed `"Physalia"` group, and the chat switcher already sorts harness-less Chats ahead
  of the rest. What a stray gives up is the harness's own affordances — the proxy's icon row, presets,
  the Edit-Harness canvas, group-scoped grounding keyed per pipeline.
- **A transmitter outside a harness hosts its own drag arrow.** The arrow normally lives on the proxy
  because a drag cannot cross two documents; standing on the canvas there is only one document and no
  proxy, so `OutletArrowAttrib` (an `ArrowAttributeBase` adapting `IHarnessOutlet`) puts the grip back
  on the node — bottom-centre, since the right edge already carries the Signal outputs. One attribute
  covers both cases and reads residency **live** per layout/frame (`OwnsArrow`), because attributes are
  built before the component reaches a document; inside a harness it draws no grip, expands no pick
  region and starts no drag. Used by `TransmitterComponentBase` and by `HarnessOut`.
- **`OnPingDocument()` inside a harness returns the SUB-document.** Use `PhyDocuments.Host(this)` /
  `ActiveHost()` for anything meaning "the user's canvas" (grounding, placement, reports, memory
  scope); keep `ScheduleSolution`/`NewSolution` and co-resident peer lookups on the local document.
  The GhJSON library resolves its own target from the active canvas, so its writes are wrapped in
  `PhyDocuments.OnHostCanvas(...)` and its reads replaced by `GhJsonBridge.SerializeByGuids`.
- **Ownership is ours, not `GH_Document.Owner`** (a `ConditionalWeakTable` in `HarnessComponent`).
  Setting `Owner` makes Grasshopper paint its own cluster icon whose menu disposes the document.
- **The proxy wears its Chats' emoji as its icon** — one per Chat inside, in the same order as the
  chat window's switcher row (by pivot, left-to-right then top-to-bottom, matching
  `ChatWindow.CompareChats`), so the node and the row of circles read as the same list. The capsule
  widens to fit the row (`HarnessComponent.Chats` → `HarnessAttrib.ContentWidth`). A harness holding
  no Chat keeps the plug-in's own mark; nickname display mode is untouched.
- **A harness has one OUTLET per transmitter inside it** — its only kind of output, since no dataflow
  crosses. `IHarnessOutlet` (implemented by `TransmitterComponentBase`) is that type: a short label
  drawn beside the grip (`"node"`, `"py"`), its own wire gradient, settled endpoints, and the drop.
  `HarnessComponent.Outlets` orders them by pivot INSIDE the harness (top-to-bottom, then left-to-right
  — stable, nothing serialized, re-ordered by moving the nodes), and `HarnessAttrib` composes one
  `ArrowGrip` per outlet down the right edge, growing the capsule taller to fit. Adding a transmitter
  inside expires the proxy layout via the sub-document's `ObjectsAdded`/`ObjectsDeleted`. New
  transmitter kinds (IronPython, VB) derive from `TransmitterComponentBase`, or from
  `ScriptTransmitterBase` when they push into an existing component on the canvas (that tier owns the
  linked guid, its persistence, the picker menu and `ResolveTarget`; supply `TargetKind` +
  `IsLinkTarget`). **`OutletLabel` is fixed text on the script/component transmitters but LIVE on
  `HarnessOut`, which returns its input's nickname** — so the grip is labelled with whatever the user
  called the wire inside. `DrawOutletLabels` reads it every frame so no push is needed; what a rename
  does need is `OnOutletRenamed` → `ExpireProxyLayout`, because the right-edge label strip is
  MEASURED now (`TextRenderer` + `GH_FontServer.Standard` — the unadjusted font, since layout runs in
  canvas units and does not re-run on zoom) rather than the old fixed 30u for three-letter tags. The
  capsule is sized from its PARTS — input column + gap + centre + gap + label column — and no longer
  floors on GH's own `bounds.Width` once there are inputs: GH's layout reserves an icon region of its
  own, this class adds another, and taking the larger left a hole between the icon and the outlet
  labels with all the slack on one side. The centre is measured in BOTH display modes
  (`CentreStripWidth`): the emoji row (or the plug-in mark, for a harness with no Chat) under icons,
  the nickname under `GH_FontServer.Large` otherwise — unadjusted, same canvas-units reason as the
  outlet labels — so the two modes size and centre identically.
- **A harness has one INLET per Harness In inside it** — its only kind of input, and the mirror of the
  outlets. `IHarnessInlet` (implemented by `HarnessIn`) is that type; `HarnessComponent.Inlets` orders
  them by pivot INSIDE the harness exactly as `Outlets` does, and the proxy grows one `Param_Inlet`
  (hidden generic param, **tree access**, optional) per node, sharing ONE nickname with that
  node's OUTPUT parameter — both start "Data", and renaming either end renames the other (the
  node's own nickname is not involved and stays free to say what the node is). **Bound by `InstanceGuid`, never by position** — and this is where the outlet pattern must
  NOT be copied: an outlet's grip is an arrow we paint, with no place in GH's graph, so it can be
  reordered and rebuilt freely; an inlet's param is a real object other components' wires point AT, so
  rebuilding one drops its wire and re-binding by index silently swaps one node's data for
  another's. `SyncInlets` therefore REUSES a param whose node still lives and reorders by moving
  the param objects (sources travel with them); `Param_Inlet.InletId` persists the binding through
  save/load, and `HarnessComponent` implements `IGH_VariableParameterComponent` (both `Can*Parameter`
  false — no zoom +/- icons; the set is derived) so an archived param set is restored rather than
  discarded. Sync is deferred to `RhinoApp.Idle` (it mutates the param set, which must not happen
  inside a solution) and is triggered by the sub-document's `ObjectsAdded`/`ObjectsDeleted`, by
  `AddedToDocument`, by `Adopt`, and by each node's own `ObjectChanged` — a **rename** and a
  **move** change what the proxy must show and reach no solution anywhere, the same class of problem
  Script I/O has. The proxy's `SolveInstance` hands each inlet's tree to its node and, when
  anything changed (`TreeIdentity`), schedules ONE solution on the harness document with those
  nodes expired — deferred, because the harness is a different document with its own solver.
  `HarnessAttrib` must LAY OUT and DRAW the input rows itself: it composes its capsule by hand and
  never reaches `GH_ComponentAttributes`'s render, so the params would otherwise be wireable and
  invisible; and because GH sizes the capsule from the params *before* the class grows it for the
  outlets, the rows are re-centred by pure translation (`ShiftInputParams` — Bounds and Pivot both, so
  the input grip moves with them). **Two traps, both found live (2026-08-18).** (1) Composing the
  Objects channel by hand means `base.Render` was never called *at all*, so GH's own render — which
  draws the wires ARRIVING at the inputs — was skipped; invisible while a harness had no inputs, and it
  looks like "data transfers but no wire is drawn", since delivery is the solver's business and has
  nothing to do with what is painted. Every non-Objects channel must fall through to `base.Render`.
  (2) **`GH_DocumentObject`'s `NickName` setter raises NOTHING** — verified against the shipped
  assembly, the setter body is a bare field assignment; only the right-click name box announces a
  rename (`Menu_NameItemTextChanged`/`Menu_NickNameChanged`), so an F2 or properties-panel rename
  reaches no handler anywhere, and nothing at all is raised for a MOVE. Worse, **`PerformLayout` is
  called from a bare handful of places and the paint loop is not one of them**, so reconciling at
  layout time sits unfired indefinitely — an `ExpireLayout` is not a promise that `Layout()` runs
  (layout is performed on SOLUTION, not on paint). The hook that works is **overriding the virtual
  `NickName` setter** (declared on `GH_InstanceDescription`), which both ends inherit from the shared
  `Param_LinkedName` base: `Param_HarnessPort` (the inside end) relabels the input via
  `OnInletRenamed`, and `Param_Inlet` renames it back via `RenameInlet` — one name, either end
  editable, the recursion cut by an equality guard, a cleared name normalised back to "Data" rather
  than obeyed. Order drift from a MOVE has no hook at all, so it is checked in `SolveInstance` and
  handed to the idle sync.
  **Do not build a rename watch on `ObjectChanged`, do not assume `ExpireLayout` will get `Layout()`
  called, and if the name is editable at both ends make the sync two-way — a derived-only name silently
  reverts what the user typed on the proxy.** **Every Rhino 8 script component wears the same `IScriptComponent`** — only its
  `LanguageSpec` tells Python 3 from C# from IronPython — so a script transmitter's `IsLinkTarget`
  MUST test the language (`GhPythonBridge.IsPython3Component` / `IsCSharpComponent`), or it will
  cheerfully push Python into the C# component next door.
- **Presets are stock `.gh` files** in `Files/PRESETS`, each one a harness's worth of pipeline —
  exactly what saving from inside a harness produces. Loading one adds a NEW harness holding it.
  The library is split three ways (`PresetLibrary`): **`Physalia/`** (shipped), **`User/`** (saved by
  the user), **`Community/`** (reserved, empty). Nothing outside those folders is listed. Wire values
  are library-relative (`User/mine.gh`) and resolved by MATCH against the enumerated library, never by
  composing a path. **Save Harness as Preset…** writes to `User/` — on the proxy's right-click menu and
  on the **Harness** widget pill (second in the top-left column inside a harness, under "Back to
  document"); it refuses a harness with no Chat, since the loader would reject it. The same two menus
  carry its reverse, **Load Harness from .gh File…** (`HarnessComponent.LoadFromFile`), which reads ANY
  `.gh` — not just one in the library — and REPLACES this harness's contents with it: the file is read
  exactly as a preset is (fresh ids, host targets cleared), one carrying no Chat is refused, and a
  non-empty harness asks first, because the pipeline going out takes its conversation and solve state
  with it and none of that is on the undo stack. The swap adopts the new document FIRST (so anything
  reacting to the old one being dismantled already sees the replacement), re-points the canvas when you
  are standing inside, and then RETIRES the old document — `RemoveObjects` + `Dispose`, so every
  `RemovedFromDocument` runs and warm CLI sessions and host-document subscriptions are actually
  released. The chat window is put back on this harness only if it was watching it (`ChatWindow.IsViewing`,
  reached through `Chat.ActiveWindow`); on Home it stays on Home.
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

## MCP — Physalia is a CLIENT (built 2026-08-27)

Physalia connects to **other people's MCP servers**; it is not one. An MCP connection is **NOT a
transmitter**: a transmitter is the harness *outlet*, driven by the *pipeline's* control flow and
writing into the user's GH document. An MCP call is driven by the *model's* control flow and must
return inside the same assistant turn, so it belongs to the **LLM Tools** tier. Side-effect-ness is
not what makes a transmitter; direction across the harness boundary is.

MCP's three primitives land on three different tiers: **tools** → LLM Tools (built); **resources** →
Grounding (not built); **prompts** → System Prompt's `Additional Prompt` (not built).

### THE SDK CANNOT RUN INSIDE RHINO — measured, and no packaging change fixes it
- **Rhino 8 runs on the .NET 8 shared runtime (8.0.30)** even though the plug-in targets net7.0.
- **`System.Text.Json` is served by the SHARED FRAMEWORK** (`Microsoft.NETCore.App\8.0.30`, v8.0.0.0)
  — *not* Rhino's own `Program Files\Rhino 8\System\System.Text.Json.dll` (7.0.0), and *not* any
  copy deployed beside the `.gha`. It is a framework assembly, so **the app-local copy is never
  consulted**. No binding redirect, no ILRepack denylist entry, nothing can change this.
- `ModelContextProtocol.Core` has no net7.0 asset → net7.0 resolves the **netstandard2.0** one, whose
  dependency group demands `System.Text.Json 10.0.10` + `Microsoft.Extensions.AI.Abstractions`. The
  cctor of `Microsoft.Extensions.AI.AIJsonUtilities` calls `JsonElement.Parse(ReadOnlySpan<byte>,
  JsonDocumentOptions)` — a **.NET 10** addition — and throws `MissingMethodException`. Downgrading
  does not help: the oldest version on the feed already wants the 10.x line.
- **The SDK's TYPES load and construct fine; only the JSON layer is dead** — which is total, MCP being
  a JSON-RPC protocol. A "does it load?" test reports success. **Any future probe of a third-party
  package in Rhino must EXECUTE a real code path, and must isolate each stage behind a
  `[MethodImpl(NoInlining)]` method invoked through a delegate**, because `TypeLoadException` /
  `MissingMethodException` fire when the *enclosing* method is JIT'd and would sail past every
  `catch` in `SolveInstance`.

### The shape that follows: one transport in-process, a bridge for the rest
- **`Physalia.Core/Mcp/`** implements the **stdio transport only** — `McpSession` (JSON-RPC 2.0 over a
  warm subprocess, background read pump, ids correlated through `TaskCompletionSource`s) and
  `McpConnections` (pool keyed by `McpServerDefinition.Identity`, idle reaper, `ProcessExit`
  teardown). Same lifecycle contract as the CLI providers, **zero new package references**.
- **`Physalia.McpBridge`** (net8.0 exe, staged to `Bridge/Physalia.McpBridge.exe`) reaches **remote /
  OAuth-protected** servers. It is a **relay, not a second MCP implementation**: stdin → the SDK's
  `HttpClientTransport` → stdout, verbatim. All MCP semantics stay in Core. net8.0 because Rhino
  already brings that runtime, and it **pins `System.Text.Json 10.0.10` explicitly** — an ordinary
  app resolves from its own `deps.json`, so there the pin actually wins. **stdout is the protocol;
  every diagnostic goes to stderr.** Never merged by ILRepack (it lives in a subfolder;
  `RepackInputDll` only globs `$(TargetDir)` itself).
- A `url:` entry launches the bridge transparently; a missing bridge is reported only when a remote
  server is actually asked for, so a stdio-only install is fully usable.

### Component + config
- **`McpServer`** (`LlmTools/`) — one node per connection, one generic class, **nothing per service**.
  It is **the only node that advertises MANY tools**, which is why `LlmToolComponentBase` grew
  `Definitions` (plural, virtual; `Definition` stays the override for every other node) and the Tool
  output became `GH_ParamAccess.list`. Tool names are **namespaced `{server}__{tool}`** and sanitized
  to `^[a-zA-Z0-9_-]{1,64}$` — two servers exporting `search` would otherwise collide on one Router
  key; `LocalName` maps back through the discovered set, since sanitizing is lossy.
- **Router dispatch matches a SET**: `ToolOutputSlot(OutputName, ToolNames)`, and an unmatched call is
  told the **tool** names, never the output names. An output serving one tool is still named after
  it; one serving many takes the node's nickname, de-duplicated because the name is the dispatch key.
- **`Router.InspectConnection` must read `LlmToolComponentBase.AdvertisedDefinitions`, NEVER the Tool
  output's `VolatileData`** (fixed 2026-09-03 off a signal trace; it read VolatileData originally).
  Volatile data is cleared at the start of every solution and refilled only when the node itself
  re-solves — but a signal-driven dispatch expires the **Router**, not the tool node upstream of it.
  So `SyncToolOutputNames`, which also runs at SolutionEnd just after the node solved, saw the whole
  set, while `DispatchToolCalls` in the next scheduled solve saw NOTHING and fell back to
  `new[] { output.NickName }`. **That fallback is right by coincidence for a one-tool node** — the
  output has already been named after its tool — which is why this survived until MCP, the one node
  advertising many: there the output is named after the NODE, so every call was answered
  *"The tool `notion__notion-fetch` does not exist. The available tools are: MCP Server."* — handing
  the model an output name, the exact thing `ToolDispatchRound` takes care never to do. **The pure
  layer was innocent and fully tested throughout** (`McpDispatchSlotTests` even asserts that error
  names tools, not outputs), so no Core test could have caught it: the policy was correct and the GH
  adapter fed it wrong data. The general lesson for any component reading a PEER's output: within a
  signal-driven solve that peer has not re-solved, so ask the component, not the solver.
- **`Files/MCP_SERVERS.YAML`** holds the servers, in the **standard `mcpServers` block** (JSON form
  accepted too, so a `claude_desktop_config.json` pastes in whole). **Physalia ships zero server
  definitions** — a catalog is explicitly not this plug-in's job. `${VAR}` expands from the
  environment; an unset one keeps its literal, because a blank reads as a configured-but-empty
  credential. **Gitignored like `API_KEY_CONFIG.YAML`**: it holds tokens in `env`, and that is exactly
  why servers live in a file and not on the component — a definition serialized onto a node would
  ship inside every preset made from it. What DOES live on the node: which server is picked and which
  of its tools are advertised (see Settings ownership).
- **Six recognised keys, in two transport-shaped halves** — `command`/`args`/`cwd`/`env` for a local
  stdio server, `url`/`headers`/`scope` for a remote one. The local half is what nearly every
  published server is, so anything offering "a URL and a key" would refuse most of the ecosystem.
  `headers` is where a **static bearer token** for a remote server goes and `scope` narrows the OAuth
  sign-in; both are ignored on a local entry, whose credentials belong in `env`. Both are folded into
  `McpServerDefinition.Identity` for the same reason `env` is — a warm bridge process authenticated
  with the old token must not serve the new definition — and both reach the server ONLY through the
  bridge's `--header Name=Value` / `--scope` arguments, added in `McpSession.StartProcess`'s remote
  branch. Most hosted servers need neither: the bridge signs in over OAuth, so blank is the normal
  case, not the exception.
- **OAuth tokens are cached on disk by the bridge, and that is what makes an early sign-in worth
  anything.** `ClientOAuthOptions.TokenCache` was unset, so the SDK kept tokens *with the transport*
  — and the bridge is short-lived by design (`McpConnections` reaps an idle session after ten
  minutes; every Rhino restart kills the pool), so the user faced a browser sign-in on nearly every
  cold start. `FileTokenCache` (bridge-side) stores them under
  `%LOCALAPPDATA%/Physalia/mcp-auth/<sha256 of endpoint+scope>.tok`, **DPAPI-encrypted with
  `DataProtectionScope.CurrentUser`** on Windows and plaintext-with-owner-only-mode elsewhere (DPAPI
  is Windows-only). **The `ClientId` must be persisted alongside the refresh token, not just the
  tokens** — it is what the dynamic client registration produced, and the SDK restores it from the
  cache so a cold start can redeem the refresh token without re-registering *and without prompting*;
  dropping that field silently reintroduces the sign-in it was meant to remove. `GetTokensAsync` is
  on the request hot path ("invoked for every request"), so the disk read and DPAPI decrypt happen
  ONCE and are held in memory. Every failure path returns "no cached token" rather than throwing — an
  unreadable cache is exactly as recoverable as no cache, while an exception would take down a
  connection that was otherwise fine. The file name is a HASH so a directory listing does not leak
  which services the user has connected to.
- **Two Mac-port items live in this stack, and only one is a break** (noted 2026-09-03; memory note
  `mac-port-mcp-gaps`). **`McpServer.BridgeExecutable()` hardcodes `Physalia.McpBridge.exe`**, but a
  net8.0 console app's apphost on macOS is `Physalia.McpBridge` with **no extension** — so the probe
  fails, the method returns null, and EVERY remote server reports the bridge missing on a build that
  is otherwise healthy. Local stdio servers keep working, which is what will make it look like a
  server-specific fault rather than a platform one; it needs to probe both names (or fall back to
  `dotnet Physalia.McpBridge.dll`). The DPAPI token cache above is the *safe* one — it already
  branches to plaintext + owner-only mode off Windows, and the proper Mac answer is the Keychain, so
  do not "fix" it by dropping encryption on Windows to make the platforms match. Already Mac-safe and
  not worth re-auditing: `McpExecutable.Resolve` guards PATHEXT behind `IsWindows`, `CopyMcpBridge`
  globs `**\*` so it stages whatever the apphost is called, `LocalApplicationData` maps to
  `~/.local/share`, and `UseShellExecute = true` opens a browser via `open`.
- **The chat window's Home screen edits this file** ("Configure MCP connections", also on the header
  menu). Two invariants there, both load-bearing. (1) **The file is EDITED, never regenerated** —
  `McpConfigEditor` replaces only the edited entry's line range, so the shipped commentary, the
  user's own notes, the entry order and the file's indentation style all survive; an entry's span
  deliberately stops before any trailing blank/comment lines, because a comment describes the entry
  BELOW it and deleting a server must not take its neighbour's documentation. (2) **The editor reads
  values UNEXPANDED** (`McpConfigEditor.ParseRaw` → `McpServerLibrary.Parse(expandEnvironment:
  false)`). Populating the form from expanded values and saving would write the resolved token into
  the file that `${VAR}` existed to keep it out of — a silent credential leak on the user's next
  unrelated edit. **The JSON form is read but never written**: it is a config shared with another MCP
  host, so `DescribeWriteBlock` refuses it and the page goes read-only rather than converting it.
- **The page also SIGNS IN**, which is the point of configuring a remote server there at all: "Save &
  sign in" (and a connect button on each list row) calls `McpConnections.GetAsync` + `ListToolsAsync`
  right then, so the browser handshake happens during setup instead of on the first solve of a node
  the user has not placed yet. It doubles as a connection test — what comes back is the tool count,
  so a wrong URL or an unresolvable command is caught immediately. Three details: the sign-in flag
  rides **on the save verb** (`?signin=1`) rather than being a second call from the page, because
  connecting has to read the entry back off disk and the page cannot know when the write landed; the
  timeout is **five minutes, not the node's two**, because a consent screen runs at human speed; and
  this is the ONE place on the page that reads values **expanded**, since it is a connection rather
  than an edit and a `${VAR}` must resolve to the credential it names.
- **`McpExecutable.Resolve` is load-bearing on Windows.** Practically every published config says
  `command: npx`, those are `.cmd` shims, and `CreateProcess` does **not** apply `PATHEXT` — bare
  `npx` throws `Win32Exception`. PATHEXT variants are tried **before** the bare name, because npm
  installs an extensionless Unix shell script next to `npx.cmd` and picking it yields "not a valid
  application for this OS platform". Both failures were hit live.
- Physalia declares **no `sampling` and no `elicitation`** in the handshake: sampling would let a
  third-party server spend the user's tokens through an LLM Call with nothing on the canvas recording
  it. A server-initiated request is still **answered** (`-32601`) — an unanswered one blocks that side
  forever, the same trap as the Codex app-server.

Verified live against `@modelcontextprotocol/server-everything` (stdio and Streamable HTTP through
the bridge): connect, `tools/list`, `tools/call`, image attachments, pooling, teardown. **Not yet run
inside Rhino**, and the **OAuth flow is unverified** — it needs a real protected server.

---

## Settings live on the component they configure (reworked 2026-08-21)

Anything the user *sets* — which clusters the model may use, which catalog tabs are folded in, which
unit text is handed over, what wording rides with a snapshot, which tools are advertised — is stored
and serialized **on the component that owns that thing**, never on the Conversation Log. The reason is
distribution: a setting is only useful if it travels. On the component it survives a copy into another
harness, it is saved inside a `.gh`, and — the point of the exercise — it **ships inside a preset**, so
an author configures a pipeline once and every end user gets it configured.

| Setting | Owner |
|---|---|
| Catalog tab/panel selection, expose-signatures, include-legacy | `ComponentCatalogGrounder` |
| Cluster selection | `ClusterGrounder` |
| Units override (never changes the document) | `DocumentUnitsGrounder` |
| Advertise-to-the-model switch | **each `LlmToolComponentBase` node** |
| Send-with-default-message, snapshot wording | `SnapshotToolComponentBase` |
| Fail-on-warnings, pruner toggles, Picker value, Script I/O link, image paths | their own components |

- **The Conversation Log is a FAÇADE, not a store.** It keeps no setting of its own: `RefreshSettingOwners`
  resolves the components feeding its Grounding and Human Tools inputs every solve (walking *through*
  bare relay params, so a tidy-up param cannot hide the owner), getters read the owners
  (**last one wins**, matching the live-grounding caches), setters write **all** of them (so two wired
  grounders can never disagree). The chat window's API is unchanged, and so is the UI.
- **The tools "selection" is derived, not stored.** Each tool node carries its own switch, so
  `ToolsSelectionOrNull` is just "which nodes are on" (null when all are) and `SetToolsSelection` flips
  nodes. That kills the old name-keyed selection: two nodes advertising the same tool name no longer
  share one checkbox. `ToolsInUse` reports only advertised nodes on the wire but exposes `ScannedTools`
  (the whole in-use set) for the chat window's list — **a parked tool must stay listed or there is
  nothing to switch back on**, which is also why `HasToolsGrounding` asks whether a grounding is wired
  rather than whether anything is advertised. The advertise flag is folded into the grounder's
  signature, so flipping it is picked up by the same SolutionEnd watch that senses a rewire.
- **The null-vs-empty distinction is load-bearing everywhere here** — null = never configured (include
  everything / use the document's own value), empty = include nothing — and Grasshopper's archive has
  no null. `SettingArchive` (`Components/SettingArchive.cs`) writes each one as a `<key>Set` flag plus
  the value, so the discipline is stated once instead of open-coded per component.
- **Older files migrate once.** `ConversationLog.Read` still reads the keys it used to own into
  `_legacy*` fields and `ApplyLegacySettings` hands them to the wired owners on the next solve — the
  first moment the owners are known. It runs through `ScheduleStateSolve`, not a raw
  `ScheduleSolution`, because GH keeps ONE document schedule and a raw post would race the latch. The
  keys are never written again, so a re-save completes the move. **The shipped presets are exactly
  this case** — they were saved before the move.

---

## GH Component Inventory

### Built
Grouped by **ribbon section** (what the user sees in Grasshopper), with the code folder
for each. Both are 1:1 apart from spelling, and every folder is under
`src/Physalia.GH/Components/` except the Harness proxy, which lives in `src/Physalia.GH/Harness/`.
85 components.

| Section (ribbon) | Folder | Components |
|---|---|---|
| **Pipeline** | `Pipeline/`<br>(Harness: `Harness/`) | Harness (the base unit — a proxy over its own sub-document holding the pipeline; right-click "Edit Harness" to go in, double-click opens the chat window), System Prompt (system prompt assembly; takes a `Grounding` list folded into the prompt), Chat (chat window entry point; mints Prompt Signals; displays the wired Conversation Log's conversation; lives INSIDE a harness. An ordinary node on the canvas — no double-click gesture, no tint of its own: the harness proxy is the only door onto the window), Harness Notes (a Physalia-themed panel — no params, pure documentation — saying what its harness is for; double-click to edit. Its text becomes the preset's DESCRIPTION in the chat window's gallery, read straight out of the `.gh` archive by `PresetLibrary.ReadDescription` without loading the document, so `HarnessNotes.TypeGuid`/`NotesKey` are an archive contract), Conversation Log (append-only conversation log; identity-based turns via four Signal inputs — input order: System Prompt, Prompt Signal, Grounding, Human Tools, Response Signal, Feedback Signal, LLM Tool Signal), LLM Call (async LLM forward pass) |
| **Guardrails** | `Guardrails/` | Schema Validator (JSON extraction + schema validation), GH Definition Validator (GhJSON/ghpatch parse + library schema + structural integrity), Component Resolver, Required Input Check (statically knowable wiring defects: required inputs wired/internalized, multi-wire into item-access inputs, endpoint paramIndex bounds, orphan data components — full graphs and ghpatch adds), Fidelity Check (post-placement intent-vs-realization diff via the authored-placement ledger; self-sources the definition recorded at placement when its Definition input is unwired/miswired; full graphs only, patches pass through), Runtime Health Check (was Canvas Observation — errors/dead/null scan with sampled values; Fail on Warnings is a context-MENU toggle, not an input — never register an input before the base-appended Signal on a shipped RoutingComponentBase subclass, it shifts saved-doc param layouts), Geometry Observation (viewport snapshot; single Signal output via `HasFailOutput => false`), Geometry Report (text-only spatial digest: per-component bboxes, disjoint groups + gaps, containments — the non-image fidelity feedback; single Signal output via `HasFailOutput => false`. Its closing instruction is single-shot — "matches your intent → reply in prose" — UNLESS the Message input carries a Build Plan progress digest, detected by `BuildPlanParser.DigestMarker`, in which case the digest's staged instruction replaces it and leads the report) |
| **Grounding** | `Grounding/` | ClusterGrounder (.ghx cluster — scaffold), PythonGrounder (python function — scaffold), CanvasStateGrounder, ComponentCatalogGrounder, DocumentUnitsGrounder, Rhino Document (`RhinoDocumentGrounder` — the Rhino-side counterpart of Canvas State: object count and kinds, the layer table, overall extents, and how many objects are SELECTED, so a phrase like "move these" resolves. It exists to delete a round trip — a script-capable model otherwise opens every session by running a probe to learn the layer table and object count, and a signal trace of the first live scripting session did exactly that. **Its refresh is unlike every other grounder and the difference is load-bearing**: GH expires along its own data graph, and a change to the RHINO document is not on it — editing geometry in Rhino runs NO Grasshopper solution anywhere, host or harness. So it watches thirteen `RhinoDoc` events, and each handler does exactly one thing: `ExpireSolution(false)`. Marking dirty is ENOUGH, because this sits upstream of the Conversation Log and the solve the user's next prompt causes recomputes it before the prompt is assembled; posting a `ScheduleSolution` would be the Script I/O trap, since a sub-document is only re-enabled when its proxy solves and a disabled one silently drops scheduled callbacks. **It also has NO throttle, deliberately** — Canvas State rate-limits because its watcher must serialize the canvas before it can tell whether anything changed, whereas this handler does no work at all, so a script adding 500 objects costs 500 flag sets and one rescan. Units are deliberately NOT included: that is DocumentUnitsGrounder's job and would otherwise be said twice in two voices. Everything is capped (25 layers, 8 type buckets) so a 10,000-object file contributes a section the size of a 10-object one, and it crosses into Core as strings and ints because Core has no Rhino reference), Image Sources (`ImageSources` — collects pictures from disk or clipboard and hands them on as `GH_ImageSource` for a model that can see; the source of the `/<alias>` prompt references. Renamed from "Image Gatherer", and it sits here rather than on a Resources tab), Tools Present (`ToolsInUse` — scans Router-wired tool nodes, emits `ToolsGrounding` for the ones whose own Advertise switch is on, and exposes `ScannedTools` (advertised or parked) for the chat window's list; holds no settings of its own; lives here, not under LLM Tools, because its output is grounding. It also collects each advertised node's optional **`GroundingDirective`** — a standing instruction about USING that tool, rendered after the tool-name list in the same prompt section — which is what turns a tool the model *may* call into one it *must* (only the Memory tool overrides it today). A directive rides in the PROMPT, not in the tool definition, because a provider's tool description is read once the model is already weighing the call and a prompt is read before it decides; it is carried per advertised node, so parking a tool takes its directive with it. **The Conversation Log REBUILDS the tools grounding** from its `_liveTools`/`_liveToolDirectives` caches in `BuildGroundedSystemPrompt`, so anything ToolsGrounding carries must be cached there too or it is silently dropped on the way to the model), Set Script I/O (`ScriptIO`, shown on the ribbon as **Set Script I/O**; renamed 2026-08-11 from "Interface Lock" — class/file/attrib renamed with it, prefixed "Set " 2026-08-24 in the display name only, ComponentGuid `B7D2F4A9-…0A46` pinned; grip-links to **any `ScriptTransmitterBase`** (Py or C#) via its own bottom arrow/gradient wire, reads that transmitter's target script component and emits `ScriptInterfaceGrounding`: the exact inputs (name/type-hint/access) and outputs (name/access) rendered as verbatim-copyable submission-JSON entries, declared LOCKED, **plus what the canvas DOWNSTREAM of each output already demands** (`GhPythonBridge.GetOutputRecipientTypes` walks each output's `Recipients` and reports their `TypeName`s — so an untyped `wall_out` plugged into a Mesh param tells the model to assign a Mesh). Two traps there: a **Panel reports `TypeName` "Text"** but accepts anything and stringifies it, so panels are excluded or every debugging wire would order the model to stringify geometry; and GH calls an Interval **"Domain"**, the one name that doesn't already match the hint vocabulary. Unknown/`Generic Data` recipients are reported as no constraint rather than guessed at. Symmetrically, `GetInputIncoming` reports the **live data** on each connected input (`VolatileData` count/branches + the goo's own `TypeName`, falling back to the source param) — "2 Curves" — and the grounding states the mismatch when the declaration disagrees ("declared item but 2 items arrive… use list"). Signature keys incoming by **shape** (type / one-vs-many / flat-vs-tree), never exact count, or every slider tick would re-solve the grounding and expire the Conversation Log. **The lock now freezes NAMES only** — the model MAY correct a type hint or access, and `ScriptTransmitterBase.ApplyLockedInterfaceAdjustments` applies both IN PLACE on push (`UpdateConverter` for the hint, the access re-stamp for the mode), so the wires survive; without that a lock is a ratchet that reports the problem and forbids the fix. The grounding wording was changed to match. Both are rendered as PROSE, never as a `type` on the output entry — the schemas set `additionalProperties:false` there, so a copied entry carrying a type would fail validation. Wiring a component to an output expires the downstream and runs a HOST solution, so watch (2) already senses it — but only because the wiring is folded into `CurrentSignature`; the same link makes the transmitter enforce the contract — enforcement (`ActiveInterfaceLock` / `RespectsLockedInterface` / the feedback) lives on `ScriptTransmitterBase`, shared. **A parameter set is language-neutral; the prose about it is not** — what the model is told comes from the transmitter's `ScriptInterfaceDialect` (component kind + schema name + code rule; `ScriptInterfaceDialect.Python` / `.CSharp` in Core), never from a branch in the lock. It has no inputs, so nothing in the pipeline ever expires it — it refreshes off **three** watches, and each one covers a case the others structurally cannot: (1) `SolutionEnd` on the **local** document = the LINK changing (the transmitter is a harness peer, so re-pointing it re-solves here); (2) `SolutionEnd` on the **host** document = adding/removing a param or changing access (the target sits on the user's canvas, so those solve the HOST and leave the harness untouched — one subscription sufficed before the harness split and is a stale-contract bug after it); (3) **`ObjectChanged` on the target component AND each of its params** = a RENAME, which reaches no SolutionEnd anywhere; its `Layout` event additionally re-arms the whole subscription, because a push REPLACES the param objects (`UpdateInput/OutputParameters` rebuild rather than mutate) and would otherwise leave every handler on a discarded param from the first push onward — the component survives that rebuild, so it is the only safe anchor. **Two traps, both tried and both regressed it to detecting nothing: `Layout` must NOT trigger a contract check (it fires mid-rebuild and would sample a half-built interface into the last-emitted signature), and the scheduled callback must stay `ExpireSolution(false)` — GH flushes scheduled delegates at the START of the next solution, so marking expired is exactly enough, while `true` asks for a solution from inside one and is re-entrant. **Why a rename is different:** GH expires along the data graph, and a param's recipients are downstream of it. Renaming an INPUT expires the component (the input's recipient IS the component) → solution → caught by (2). Renaming an OUTPUT expires only what is wired below it — the component is UPSTREAM of its own output — so an *unwired* output rename expires nothing and runs no solution at all. On a script component the param name IS the variable name, so that is a real contract change. Watches (2) and (3) are re-pointed on every solve and every callback, since the harness owner is not always resolvable at `AddedToDocument` time and the param set itself changes. **A change detected this way MARKS the component expired (`ExpireSolution(false)`) and must NOT post a `ScheduleSolution` to the harness sub-document** — the edit happens on the user's canvas, so the host solves and the harness does not; a sub-document is only re-enabled when its proxy solves and a disabled one ignores scheduled solutions, so the callback is dropped and the stale contract reaches the next inference. Expiring is enough: this component is upstream of the Conversation Log, so the solve the user's next prompt causes re-solves it first. **The same trap applies to any harness-resident component reacting to a host-side event.** Enforcement needs none of this — `RespectsLockedInterface` reads the live specs at push time. Disabling the component suspends the lock without unlinking) — all emit `GH_Grounding` for the Conversation Log's Grounding input. `Grounding` is a Core discriminated union (`ComponentCatalogGrounding` migrated from System Prompt's old catalog input; `GH_Grounding.CastFrom` adapts producer goo like `GH_ComponentCatalog`) |
| **LLM Tools** | `LlmTools/` | Model-callable tools (`LlmToolComponentBase : StatefulComponentBase`; **a tool result is TEXT on every provider**, so a tool answering with an image returns it as an ATTACHMENT — `ToolCallResult.OkWith(text, blocks)` → `ToolCallOutcome.Attachments` → `ToolBatchRunner` → `Router` → `ToolDispatchRound.CombineResults` → `ConversationLogBuilder.RecordToolSignal`, riding the SAME answering user turn as a sibling block. **Attachments must sort after every `ToolResultContent`** (Anthropic requires tool_result blocks to lead that turn); the runner and the Router each enforce it. No provider change was needed: Anthropic puts tool_result + image side by side, the OpenAI protocol already splits such a turn into role:tool messages plus a role:user message, Gemini emits functionResponse + inlineData parts, `ToolPairing` only reports, and compaction's `Reassemble` keeps non-tool blocks via its `default` arm. Before this, the Router filtered to `ToolResultContent` and `RecordToolSignal` dropped non-call blocks — an image was silently lost in BOTH places; Core type `LlmToolDefinition`, goo `GH_LlmToolDefinition`, param `Param_LlmToolDefinition`; the base owns Tool(0)/Result(1), the per-node **Advertise To The Model** switch (right-click, and the chat window's tools page — a parked tool stays wired and able to answer but is never mentioned to the model, so it is never called; see Settings ownership) and, since 2026-08-17, `RegisterAdditionalOutputs` + `OnSolveEnd` — publish a call-mutated output from `OnSolveEnd`, not `OnSolveTick`, which runs before the calls and leaves the wire a solve behind): WebSearch, ReadUrl, MemoryTool (`memory` — file-backed notes under `Files/MEMORIES`; a GLOBAL folder shared by every pipeline plus a LOCAL one the user **NAMES on the node's own `Memory Folder` text input** (`MemoryLocations`, 2026-08-25). **The local folder is never derived** — not from the .gh file, not from the harness nickname. Both derivations were tried and both failed the same silent way: the file key left a harness's notes behind when it was saved out as a preset, and the nickname key put every unrenamed pipeline into one folder called `Harness`, because that is the default nickname and nobody looks at it. A derived key is only as good as the thing it derives from, and both of those default. The typed name is ordinary internalized param data, so it is saved in the .gh **and carried inside a preset** — which is what actually makes the notes travel with the pipeline — and two Memory tools given the same name share one set, which is how a rebuilt pipeline resumes. Blank falls back to the node's **`InstanceGuid`**: unique, stable across save/load so an untouched node keeps its notes, and obviously not a name, so it cannot be mistaken for one or silently collide. `MemoryLocations.FolderKey` is the only place a name is sanitized (invalid chars + whitespace → dashes, leading/trailing dots and dashes trimmed — which is also the `..` containment guard). Note the input could be added at all only because `LlmToolComponentBase` registers Signal **FIRST** (index 0) and appends subclass inputs after it — the opposite of `RoutingComponentBase`, where Signal is last and a new input shifts saved-doc layouts. It is also the only tool overriding `GroundingDirective`, which makes reading memory before answering mandatory — advertising it is not enough on its own, since the model decides for itself whether a call is warranted and on a self-contained-looking request it decides not to), Create/Ref. Rhino Geometry (`RhinoGeometryTool` — renamed 2026-09-02 from "Rhino Geometry", DISPLAY NAME ONLY with ComponentGuid `7D3F1A94-…8A21` pinned and the class/file/nickname untouched, exactly as the Set Script I/O rename was done. The node both CREATES geometry in the Rhino document and drops a canvas param REFERENCING it, and the second half is the point: it is what hands the definition a real Rhino input. Distinct from a Rhino-control tool, which acts on the Rhino document and leaves nothing on the canvas), Run Rhino Script (`run_rhino_script` — the model runs **Python 3 against the live Rhino document** through `Rhino.Runtime.Code`, the Script Editor’s own engine, which was ALREADY a compile-time reference for `GhPythonBridge`: no new dependency, and `RhinoScriptRunner` (Generation/) is the whole engine wrapper. **The streams are bound directly** (`RunContext.OutputStream`/`ErrorStream` assigned to our own buffers), which is the point of the node: `print` IS the read-back, so the model asks whatever it wants to know by writing three lines of Python and gets the answer in the SAME round — and that is why Physalia ships no separate document-inspection tool. The Rhino MCP server needs a 156-line `get_context` precisely because it lives outside Rhino, can only drive the engine through `_-ScriptEditor _Run` on a temp file, and must scrape `CapturedCommandWindowStrings` — which is also why its own description has to warn the model not to trust `scriptcontext.doc`. In-process the failure is a structured `CompileException`/`ExecuteException` (message + `Position` + stack trace), never a search for the text "Traceback". **The whole run is ONE undo step**, owned with an explicit `BeginUndoRecord`/`EndUndoRecord` rather than left to `RunContext.RecordDocumentUndo`, so the label is ours and the behaviour does not depend on the engine’s default. **The object count before/after is reported on EVERY path including failure** — a script that raises half way through has already applied what it did, and assuming otherwise is how a retry doubles the geometry. `RunsAsync => true` NOT for speed: document mutation is illegal off the UI thread AND inside a solution, so the run is marshalled to `RhinoApp.Idle` exactly as Take Snapshot is; the timeout bounds waiting for Idle to ARRIVE, never the script itself, because a managed thread cannot be aborted and a runaway script blocks Rhino exactly as the same script would in the Script Editor. `Last Script` output publishes the source from `OnSolveEnd` (not `OnSolveTick`, which runs before the calls). Python 3 only — the runner is language-parameterised internally, so C# is a one-line addition once tested. The tool description deliberately steers the model AWAY from using it for parametric work: geometry that should be driven by the graph belongs on the canvas where it stays editable and gets validated), ComponentSearch, RhinoCommonSearch, Take Snapshot (`take_snapshot` — the model LOOKS: a camera stands at the wired `Current Location`, aims anywhere over the full sphere by `azimuth` (0=+Y forwards, 90=+X right, clockwise in plan — the SAME bearing convention as the movement cones, shared structurally via `SpaceNavigator.Aim`/`BearingLabel` so one mental compass covers walking and looking) plus `elevation` (−90..90, clamped not wrapped; azimuth IS wrapped), captures, and hands the image back. 35mm-equivalent lens ≈ 54° horizontal FOV, and the model is TOLD that number so an off-camera thing is not read as an absent thing. Outputs `Current View` (the latest aim on its own, left EMPTY before the first look — a zero vector would read downstream as a real direction) and `Snapshot Directions`, a TREE with one branch per VISIT to a Current Location (revisits open a new branch — branch order stays walk order, which downstream can collapse by point but could never re-split). `RunsAsync => true` NOT for speed but because posing a viewport is illegal inside a solution and must be on the UI thread: the capture is marshalled to `RhinoApp.Idle` and awaited off the solve, with a timeout so a never-idle Rhino cannot hang the round with its tool id unanswered. `ViewportSnapshot.TryCaptureFromCamera` borrows the user's viewport and restores it exactly via Rhino's own `PushViewProjection`/`PopViewProjection` in a `finally`), Move In Space (`move_in_space` — walks the model one step at a time through a user-supplied `Positions` lattice from a `Start Point`, publishing the route on `Traversed Points` plus `Current Position` (where it stands now, on its own so a camera wires straight to it — this is what feeds Take Snapshot's `Current Location`). That geometry is the point of the tool: it turns the model's spatial reasoning into something the definition can build on. Optional `Position Notes` gives each position a description reported to the model whenever it stands there — arrival AND look alike, since the note describes the POSITION; paired with `Positions` by Grasshopper's longest-list rule via the pure `Core/Common/ListPairing.MatchLongest` (equal lengths 1:1, a shorter list reuses its LAST note, surplus notes ignored), done in code because this component reads both as whole lists in one solve and must not iterate. Unwired = no notes. Adjacency is DERIVED, never configured — `SpaceNavigator` buckets each candidate by vertical band (same/up/down by doc tolerance) and by one of eight 45° in-plane cones, then offers only the CLOSEST per bucket, which is what makes a step a step: collinear points further along a bearing arrive one move later, and `up_*` resolves to the next level up rather than the top of the stack, with no level clustering anywhere. Eight cones means full angular coverage, so a rotated or scattered cloud is still fully navigable and nothing reachable is ever hidden. Directions are FIXED WORLD directions — forward=+Y, right=+X, up=+Z — deliberately not heading-relative: a heading is undefined on the first move, must be tracked by the model across turns, and flips left/right as it turns. The 26 tokens are generated from `SpaceNavigator.AllTokens` into the schema enum so advertised and accepted can never drift. `direction` is optional — omitting it reports position + options without moving, which is how the model gets its bearings on the first call, since nothing else tells it where it starts. An unavailable-but-real token is an `IsError` result that re-lists the legal moves, so the model never assumes a move it did not make. Walk state is session-only and restarts when the Start Point moves), Read PDF (`read_pdf` — reads PDFs the human attached in the chat, plus a standing set in the folder named on its own **`PDF Folder`** input (bare name → `Files/PDFS/<name>`, rooted path used verbatim; typed not derived, so it travels inside a preset — same reasoning as MemoryTool's). Four actions: `list`, `text` (page ranges, `max_chars`), `search` (returns page AND a normalized region per hit) and `render` (rasterizes a page or a REGION of one into an `ImageContent` attachment). **The region is the point**: an A1 sheet at 150 DPI is ~4900px wide and the 1568px delivery cap makes 4pt dimension text ~7px tall, so the loop the tool description teaches is overview → search/pick a region → render that region at high DPI. Two invariants, both easy to break silently: `PdfRegion` is normalized 0-1 with a **TOP-LEFT** origin (PdfPig reports glyph boxes bottom-up, PDFium's crop is top-down; the flip lives ONLY in `PdfRegion.FromPdfPoints`/`ToPointsTopLeft`, and inverting it renders the mirrored area, which reads as a plausible blank crop rather than a bug), and **`DpiRelativeToBounds: true` is mandatory whenever `Bounds` is set** or PDFium applies the DPI to the whole PAGE and stretches the crop across a page-sized canvas — the zoom loop then silently accomplishes nothing. A page with no text layer is reported as such and never as an empty string (`PdfTextResult.EmptyPages`), because a scan and a blank sheet extract identically and only one of them means 'look at it instead'. `RunsAsync => true` but with NO `RhinoApp.Idle` marshalling — nothing here poses a viewport, unlike Take Snapshot — and `PdfPageRenderer` holds a static lock because PDFium is not thread-safe across nodes. It is also **the one part of Physalia backed by a native library**: see the Build section's compatibility note and `planning/pdf-tools.md`) — plus **MCP Server** (`McpServer` — connects to ONE server from `Files/MCP_SERVERS.YAML` and advertises its tools; the ONE node that advertises MANY tools, since a server's set is discovered at runtime. See the MCP section below) and Router (dispatch loop). Tools Present lives in the **Grounding** section |
| **Human Tools** | `HumanTools/` | Chat-window affordances for the HUMAN, not the model (`HumanToolComponentBase : PhyBase` — passive emitters: no inputs, one `Param_HumanTool` output; Core union `HumanTool` in `Physalia.Core/HumanTools/`): Geometry Snapshot + View Snapshot (both `SnapshotToolComponentBase`, which owns the shared "Send With Default Message" context-menu toggle **and the message override** — both serialized on the tool, see Settings ownership: on = the capture is sent as its own message carrying an editable default message, off = it attaches to the prompt box for the human to caption, on its OWN image lane independent of Add Image and of the other snapshot tool. Geometry Snapshot frames the camera on transmitter-generated geometry and is armed only while such geometry exists; **View Snapshot captures the active viewport as-is — no geometry scan, no camera move, so wired is armed**), Add Image (enables image paste/drop/picker in the prompt box — image intake is fully disabled without it, except for a snapshot tool's own attach lane, see `ConversationLog.AcceptsPromptImages`), Export Conversation (header button → saves the viewed conversation as a .txt transcript; **replaced the `/export` slash command**, which no longer exists — the composer now has no built-in commands), Signal Trace (header button → opens `SignalTraceWindow`; **replaced the signal-trace canvas widget**, which was deleted. The trace log itself is still process-wide/session-wide, not per-conversation), Image Mark Up (`ImageMarkUp` — puts the chat window's image editor (`ImageEditor.svelte`) in front of every image the human sends: freehand pen, 12pt text notes, click-click arrows, an eraser, a 9-swatch palette defaulting to red, undo/redo, cancel/confirm. **Adds no button of its own** — it changes what the other image affordances do: a capture from ANY snapshot tool opens in the editor instead of leaving as-is, and each image in the prompt box grows a pencil button on its thumbnail. Marks are kept as OBJECTS in the image's own pixel space and flattened only on confirm, which is what lets the eraser lift a mark off the picture underneath (object-level: one stroke/note/arrow is one mark, and one eraser gesture is one undo step — a gesture that hits nothing takes none) and what keeps the committed PNG at full capture resolution; stroke widths and font sizes are stored in natural pixels but CHOSEN from the on-screen scale, so 12pt means 12pt as the human sees it whatever the capture's size. **Cancel means two different things by design, and the asymmetry is the whole reason send mode needed a new path:** in attach mode (and for an already-attached image) the plain image survives — only the mark-up is discarded — but a send-mode capture was never attached anywhere, so cancelling abandons it. Send mode therefore inverts: the button posts `marksnapshot`/`markviewsnapshot`, the host captures and hands the image to the page (`markUpSnapshot`) WITHOUT minting anything, and a confirm comes back as a submit payload carrying `kind: "geometry-snapshot"`/`"view-snapshot"` — routed by `SubmitJsonPayload` to `Chat.SendMarkedSnapshotFromWindow`, which re-reads the message off the wired tool. **The page is handed an image to draw on, never the text that will speak for it**, and the grant is re-checked at confirm as well as at capture, because the wire can change in between), Token Count (`TokenCount` — puts the running token count in the chat window's bottom-right corner. **Grip-links to a `TokenEstimator`** exactly as Script I/O links to a transmitter (`TokenCountAttrib : GripLinkAttrib`, `ArrowStyles.TokenCount`), and the link is the ONLY resolution path: the old "first Token Estimator downstream of the Conversation Log" walk (`PromptPipelineView.GetDownstreamTokenCount`) is **deleted**, so an estimator on its own now counts for the pipeline and shows nothing — counting and displaying are two components. Unlinked, or linked to a target that has gone, warns on the node and hides the counter. The count is read LIVE off the estimator's output through `ConversationLog.LinkedTokenCountOrNull` (an `Owners<TokenCount>` walk like every other setting owner), because the chat window asks on its own 0.15 s tick and nothing re-solves the tool when the estimator recounts. It is also the first human tool needing to say anything about itself, which is why `HumanToolComponentBase` grew `OnSolveEnd()` — a runtime-message hook that leaves the sealed emission alone). Read PDF (`AddPdf` — enables PDF intake: a rail button plus drag-and-drop. **UI-only and pointedly not the tool that reads PDFs**: attaching one registers the file in a session `PdfRegistry` and puts a short DESCRIPTOR in the turn — name, alias, page count, sheet size, which pages carry a text layer, and a best-guess sheet number read off each title-block corner — while every actual page is pulled on demand by the `read_pdf` LLM tool. That split is what makes a 400-sheet set affordable to attach. The registry is keyed on the **local `GH_Document`**, which is how the two halves find each other without walking a wire through the Router. Files are **referenced where they sit, never copied**, which is why the button's picker runs HOST-side — it is the only intake path that learns a real path, and it moves no bytes at any size; drag-and-drop is the one path that must send bytes (the DOM File API withholds the path), so it is capped at 100MB and spooled to temp. **PDF bytes never ride the `SubmitMessage.images` lane**, and a PDF is never a `PendingImage`: it gets its own chip strip, because there are no bytes to draw a thumbnail from and `ImageEditor.svelte` loads its source via `Image.src` and structurally cannot open one). Wired into the Conversation Log's Human Tools input; never touch the system prompt, never advertised to the model |
| **Models** | `Models/` | AnthropicModel/Tweaker, GeminiModel/Tweaker, OpenAICompatibleModel/Tweaker, ModelInformation, LlamaCppModelInfo, ApiKeys (+ `ModelComponentBase`, `TweakerComponentBase<TConfig>`), plus the two **local-CLI** models that take no API key and derive from `PhyBase` directly: ClaudeCodeModel, CodexModel (Model + Effort, both Picker-backed; its model list is fetched live from the CLI) |
| **Control Flow** | `ControlFlow/` | Feedback, FeedbackCollector (wireless signal transport via grip-link; deliberately breaks the GH DAG), Detect JSON (presence gate — single Signal output via `HasFailOutput => false`; attempted JSON, even malformed, passes through; plain conversation dead-ends quietly inside the component via `RoutingResult.Fail(emitSignal: false)`), Build Plan (staged generation: parses the model's `<plan>` block out of each response and renders a progress digest on a `Progress` text output for the Geometry Report's Message input — a pass-through tap, never a gate; see `planning/incremental-building.md`), Signal Limiter (caps total loop rounds), Merge Signal (joins two or more signal branches into one — variable inputs via the zoom +/- icons, minimum two, added/removed at the END only because the hold and the base's consume-once marks are index-keyed. A **join, not a passthrough**: parallel branches latch on their own scheduled solves, so emitting per solve would give one signal — and one logged turn — per branch; it holds the newest signal per wired input and mints ONE merged signal once the whole wired set is in. Merge order is global sequence (causal) order: payloads blank-line-joined, ContentBlocks combined, newest Instructions kept, outcome Failure if any part failed. **Combining blocks is not concatenation, and every aggregator (this and the Feedback Collector) must use `SignalAggregation.Combine`:** a signal's ContentBlocks are the WHOLE turn and its Payload only their text trace, so joining payload strings while merely concatenating block lists leaves a text-only branch's text in the payload and in no block — the Conversation Log then records the blocks and silently drops the text (a merged Geometry Report + Geometry Observation reached the model as the image alone, 2026-08-17). A branch with no blocks of its own therefore contributes its payload AS a TextContent block; blocks are materialised only when some branch carried them, so an all-text merge stays text-only. Unwired inputs are ignored; a round where a wired branch never fires parks at `1 / 2` until it does — `Clear Outputs` abandons it), Stall Guard (caps *identical* failure rounds — fingerprints failure payloads; escalates at the Stall Limit, suppresses re-emission beyond it; Stall Limit is input 0, single Success Signal output — parked loop = STALLED caption only, nothing emitted) |
| **Signals** | `Signals/` | ConstructSignal (manual mint), DeconstructSignal (passive inspect — never consumes), Conversation/Message/Instructions Compositors + Decompositors |
| **I/O** | `IO/` | The harness boundary as the user meets it: two plainly named nodes, each with exactly one side. **Harness In** (`HarnessIn`, no inputs, one generic tree output — placing one inside a harness grows an input on the LEFT edge of the proxy, and whatever is wired in out there arrives here tree-intact. **The output's nickname and the proxy input's nickname are ONE name** — both start "Data", rename either and the other follows; the node's own nickname is not part of it. Generic rather than geometry-typed on purpose: geometry is the main cargo and rides through untouched, but a goal condition is usually stated partly in numbers and text. **Passive — it mints NO signal and starts no round**: it latches what it was handed and re-emits it on every solve of the harness, so the value is current whenever a signal-driven round reads it. That is what makes it safe to feed a harness from a slider, and it is what stops Harness-Out-writes-canvas / canvas-feeds-Harness-In closing into a cycle GH's own detector cannot see — nothing in the pipeline ACTS on inlet data by itself. Outside a harness it has no proxy to grow an input on and says so). **Harness Out** (`HarnessOut`, one generic tree input, **no outputs** — an endpoint, not a passthrough: the pipeline ends here and the data leaves. The merge of the former Text and Geometry transmitters, which differed only in the param type each declared and between them still refused booleans, integers and colours. **The input's nickname labels the grip** the proxy paints for it (`OutletLabel`, live) — one-way, since a painted label has no editor of its own, which is the one place it differs from Harness In. **The tree is data**: it is internalized into the target branch-for-branch, which needs more than `SetPersistentData(params object[])` (that can only make ONE flat branch): `ParamTargets.WriteTree` walks the target's base chain to its constructed `GH_PersistentParam<T>`, builds a `GH_Structure<T>` by reflection, casts each item with the param's own protected `Cast_Object` (the very conversion the flat setter performs, so a Brep entering a Mesh input converts exactly as through a wire), and calls the structure-taking `SetPersistentData` overload; items the param cannot read are COUNTED and reported, never silently dropped. Its grip connects like a **standard GH output**: drop it on an input GRIP and it links that input — ANY input. Target resolution mirrors GH's own: nearest input grip (12u), else the row under the cursor, else the node's first input; a Panel or floating param links directly. **A drop on empty canvas does nothing — it never creates a target.** **Stringifying is PER ITEM and keeps the container**: `Param_String.Cast_Object` already turns each goo into text on its own (verified — a `GH_Point` casts to `"{1, 2, 3}"`), so a text param takes the tree intact through the normal path; on top of that, an item any param refuses outright is offered again as a `GH_String` of its text form. A **Panel** is the one target that cannot hold data — a `GH_Param` but NOT a `GH_PersistentParam`, its only storage is one `_userText` string — so `ParamTargets.WritePanel` writes **one item per line** and forces `Properties.Multiline` off, because `CollectVolatileData_Custom` splits that string BY LINE into one branch: a LIST round-trips exactly. **A Panel parses no paths back** (probed: `{0;0}` headers come back as data ITEMS), so a multi-branch tree is flattened with a warning pointing at a Text parameter. `CanHoldOrDisplay` is the shared "Panel or persistent param" target test. Change-detection is tree shape + **reference identity** of every goo (`TreeIdentity`) — GH re-mints goo only when its producer recomputes, so this can re-send unchanged-looking data but can never MISS a change — **except for a signal, keyed by SEQUENCE**, since a latched signal is re-wrapped in a fresh goo on every solve and identity would have it writing to the canvas on every scheduled solve. The queued tree is COPIED (`new GH_Structure<T>(tree, false)`), since the one from the solve belongs to the input param and is cleared before the idle callback runs. Delivery is deferred to `RhinoApp.Idle` — it writes into and expires the HOST document from inside a harness solve, which cannot be done in-solution. A target with a wire into it warns, since internalized data loses to a wire. **Preview is supplied by hand** (`IsPreviewCapable`/`ClippingBox`/`DrawViewport*` forwarding to any `IGH_PreviewData` on the INPUT — there is no output to read): generic params tell GH nothing about geometry, so without it the viewport preview the Geometry Transmitter had would have been silently lost). Both inside ends are `Param_HarnessPort : Param_LinkedName`, the shared base that overrides the virtual `NickName` setter so a rename is heard at all |
| **Transmitters** | `Transmitters/` | The harness's outlets — everything that writes OUT of it (`TransmitterComponentBase`, see the Harness section): Component Transmitter (`CompTx`, grip "node" — places/patches a GhJSON graph on the canvas), Harness Out (listed under **I/O**, not here: it is the general-purpose outlet and NOT a `TransmitterComponentBase`. See the I/O row), C# Transmitter (`CsTx`, grip "C#" — pushes an LLM-generated `CSharpComponent` submission into a linked **Rhino 8 C# Script** component. Same JSON shape as Python (`ScriptComponentJson` parses both), but C# declares its parameters TWICE — in the submission and in the `RunScript` signature the engine reads out of the source — so the push is gated on a signature check that rejects a disagreeing submission before anything reaches the canvas and spells out the expected signature in the Fail feedback. Lockable by a Script I/O like the Python one — the two checks compose (lock pins declared params to the target's, signature pins the code to the declared params) — except a locked C# submission must declare the interface WHOLE (`AllowsPartialInterface => false`): an undeclared param has nothing in the signature to bind to, where Python just never mentions the variable. None of PyTransmitter's marshalling repairs: they exist for the Python engine's value wrapping. System prompt pair: `PREAMBLE/C# Script.txt` + `SCHEMA/C# Script.json`), PyTransmitter (pushes generated Python into a linked Script component — linked via its right-click "Link to Script Component" picker over the HOST canvas, since a grip drag cannot cross into a harness; the drag arrow itself is hosted by the harness proxy, on the grip labelled "py"; routes its errors; when an enabled Script I/O grip-links to it, freezes the param SET but still applies hint/access corrections in place **and still runs the Python marshalling repairs** (`ApplyPythonOutputMarshalling` — No Type Hint on outputs + `MarshOutputs` on — on EVERY push: `SetScript` copies that flag off the script, so skipping them under lock reproduces "Data conversion failed from Goo to …" on any list output; they are not interface changes and stay on PyTransmitter, never the shared base). Pushes **code only** in the sense of the param set — never restructures the target's params — and rejects submissions declaring unknown input/output names with corrective Fail feedback) |
| **Tokens & Compaction** | `TokensCompaction/` | Token Estimator (`TokenEstimator` — mirrors the wired Conversation Log so the chat window can show a live count), Token Window / Token Threshold / Tokenization Techniques (the estimator's settings), and the compaction family (`CompactionComponentBase : RoutingComponentBase<Instructions>`, which SEALS `FailSignalDescription` empty for all of them): Sliding Window, Anchored Window, Content Pruner, Summarizer. Compaction sits inline between the Conversation Log and the LLM Call, re-emitting a signal carrying compacted `Instructions`; every one of them FAILS OPEN, forwarding the conversation uncompacted with a runtime message rather than stalling the turn |
| **Extra** | `Extra/` | Serializer / Deserializer (.ghjson canvas export/import via `GhJsonBridge`), Picker (a value list whose choices come from the component it is wired to; the pick is serialized on the Picker itself as `SelectedValue`. **A Picker solves BEFORE the component it feeds**, so on the first solve after a file opens the source's list is whatever it holds at construction — which is why falling back to `values[0]` is gated on `PickableInput.IsSettled`. A source whose real list arrives ASYNCHRONOUSLY must report `IsSettled: false` until the fetch completes (success OR failure), or its seed list silently overwrites the restored pick and, since the snap writes back, loses it for good — this is exactly what made the Codex Model always reopen as `gpt-5.5`, while Claude Code was fine only because its list is a fixed complete set and the HTTP models were fine only because theirs start EMPTY. An empty list never snaps either: nothing on offer is not evidence the pick is wrong, so a saved pick survives an unreachable provider. `MenuValues` keeps a currently-unoffered pick visible and checked in both menus), Zoom Guid (zooms the canvas to a component by instanceGuid — a debugging aid) |

### Planned, not yet built (spec: `planning/physalia-primitives.md`)
PyValidator, Counter, Meter, Monitor, Aggregator — note that doc's "Receiver" is a **different, superseded** thing (and the name is now retired entirely — the harness inlet is called **Harness In**) (the galapagos-wired build target that became the transmitters), not the Harness In built in 2026-08-18 — plus LLM Call alternate roles via `.skill` files (Distiller, Reflector, Interpreter, etc.).

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

### Compatibility note — the `.gha` is no longer fully self-contained (2026-08-25)
Adding PDF page rendering broke the single-file property, deliberately and with the trade accepted.
**The merge rule itself is intact** — every assembly ILRepack merges is still pure managed IL — but
the shipped artifact is now the `.gha` **plus native binaries**, and packaging has to carry them.

- **`PdfPig` (Physalia.Core) is merged normally.** Apache-2.0, pure managed, resolves its `lib/net6.0`
  assets on our net7.0 TFM. Seven assemblies (`UglyToad.PdfPig*`), no denylist entry, no special
  handling. It does text extraction, page probing and letter bounding boxes.
- **`PDFtoImage` + `SkiaSharp` (Physalia.GH) are DENYLISTED from `RepackGha`.** They are P/Invoke
  shims over `pdfium` / `libSkiaSharp`, laid down by NuGet under `$(TargetDir)runtimes/<rid>/native/`.
  The natives escape the merge only because the `RepackInputDll` glob is non-recursive; the managed
  halves would be merged and internalized, and a renamed internalized shim inside a `.gha` that
  Grasshopper loads with a plain `Assembly.LoadFrom` is where native resolution stops being
  predictable. Note this is a **different** reason from the JSON stack's denylist, which is about
  type identity.
- **`PdfNativeLibrary` (Generation/) pins the lookup** with `NativeLibrary.SetDllImportResolver`,
  installed lazily on first render. A Grasshopper plug-in gets no `AssemblyDependencyResolver` and no
  host `.deps.json` probing, so the default P/Invoke search falls back to the OS path — which has
  Rhino's directory in it and not ours. Without the resolver the symptom is a `DllNotFoundException`
  at the first render, **in Rhino only**, from a build that is perfectly healthy on the command line.
  It tries `runtimes/<os>-<arch>/native/` then `runtimes/<os>/native/` then a flattened copy beside
  the assembly — the two-step matters because PDFium files macOS under `osx-arm64`/`osx-x64` while
  SkiaSharp ships one universal binary under a bare `osx`.
- **Platforms covered:** win-x64, win-arm64, win-x86, osx-x64, osx-arm64, linux-x64/arm64. Anywhere
  else, `Install()` returns a reason and rendering reports itself unavailable **while text extraction
  keeps working** — the tool degrades rather than failing.
- **Packaging must ship the `runtimes/` tree** alongside the `.gha`, plus the loose `PDFtoImage.dll`
  and `SkiaSharp.dll`. `TrimNativePdbs` deletes the native `.pdb` files after every build
  (`libSkiaSharp.pdb` alone is ~89 MB and describes Skia's own C++ internals).
- **Verify in Rhino, not on the command line.** A console app resolves these natives through
  machinery a `.gha` does not have, so a green `dotnet build` proves nothing about this. Place a Read
  PDF tool and render one page.

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
    /Files                       ← user-alterable runtime content ONLY; every folder here is read by code
        API_KEY_CONFIG.YAML (+ .example)
        MCP_SERVERS.YAML (+ .example)   ← MCP servers; gitignored, holds credentials
        /SYSTEM_PROMPTS   ← /PREAMBLE + /SCHEMA, resolved by name from the System Prompt component
        /CLUSTERS         ← .ghcluster files + clusters.json manifest (Cluster Grounding)
        /PRESETS          ← preset harnesses (.gh — a saved harness sub-document)
            /Physalia     ← shipped with the plug-in
            /User         ← written by "Save Harness as Preset…"
            /Community    ← reserved, not populated yet
        /MEMORIES         ← memory tool: /GLOBAL and /LOCAL/<Memory Folder input, or the node's id>
        /PDFS             ← read_pdf tool: one folder per <PDF Folder input> (a rooted path bypasses this)
```
