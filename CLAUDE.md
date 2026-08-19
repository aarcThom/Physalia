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
hands us wires pointing inward for free — so a **Receiver** inside grows a real input param on the
proxy's LEFT edge (inlets). See the Receivers subsection below.

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
  region and starts no drag. Used by `TransmitterComponentBase` and by `TextTransmitter`.
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
  `IsLinkTarget`).
- **A harness has one INLET per Receiver inside it** — its only kind of input, and the mirror of the
  outlets. `IHarnessInlet` (implemented by `Receiver`) is that type; `HarnessComponent.Inlets` orders
  them by pivot INSIDE the harness exactly as `Outlets` does, and the proxy grows one `Param_Inlet`
  (hidden `Param_GenericObject`, **tree access**, optional) per Receiver, named after that Receiver's
  nickname. **Bound by `InstanceGuid`, never by position** — and this is where the outlet pattern must
  NOT be copied: an outlet's grip is an arrow we paint, with no place in GH's graph, so it can be
  reordered and rebuilt freely; an inlet's param is a real object other components' wires point AT, so
  rebuilding one drops its wire and re-binding by index silently swaps one Receiver's data for
  another's. `SyncInlets` therefore REUSES a param whose Receiver still lives and reorders by moving
  the param objects (sources travel with them); `Param_Inlet.ReceiverId` persists the binding through
  save/load, and `HarnessComponent` implements `IGH_VariableParameterComponent` (both `Can*Parameter`
  false — no zoom +/- icons; the set is derived) so an archived param set is restored rather than
  discarded. Sync is deferred to `RhinoApp.Idle` (it mutates the param set, which must not happen
  inside a solution) and is triggered by the sub-document's `ObjectsAdded`/`ObjectsDeleted`, by
  `AddedToDocument`, by `Adopt`, and by each Receiver's own `ObjectChanged` — a **rename** and a
  **move** change what the proxy must show and reach no solution anywhere, the same class of problem
  Script I/O has. The proxy's `SolveInstance` hands each inlet's tree to its Receiver and, when
  anything changed (`TreeIdentity`), schedules ONE solution on the harness document with those
  Receivers expired — deferred, because the harness is a different document with its own solver.
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
  `NickName` setter** (declared on `GH_InstanceDescription`) at BOTH ends: `Receiver.NickName` relabels
  its input via `OnInletRenamed`, and `Param_Inlet.NickName` renames the Receiver via its `Renamed`
  callback — one name, either end editable, the recursion cut by an equality guard. Order drift from a
  MOVE has no hook at all, so it is checked in `SolveInstance` and handed to the idle sync.
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

## GH Component Inventory

### Built (`src/Physalia.GH/Components/`)
| Folder | Components |
|---|---|
| **Pipeline** | Harness (the base unit — a proxy over its own sub-document holding the pipeline; right-click "Edit Harness" to go in, double-click opens the chat window), System Prompt (system prompt assembly; takes a `Grounding` list folded into the prompt), Chat (chat window entry point; mints Prompt Signals; displays the wired Conversation Log's conversation; lives INSIDE a harness. An ordinary node on the canvas — no double-click gesture, no tint of its own: the harness proxy is the only door onto the window), Harness Notes (a Physalia-themed panel — no params, pure documentation — saying what its harness is for; double-click to edit. Its text becomes the preset's DESCRIPTION in the chat window's gallery, read straight out of the `.gh` archive by `PresetLibrary.ReadDescription` without loading the document, so `HarnessNotes.TypeGuid`/`NotesKey` are an archive contract), Conversation Log (append-only conversation log; identity-based turns via four Signal inputs — input order: System Prompt, Prompt Signal, Grounding, Human Tools, Response Signal, Feedback Signal, LLM Tool Signal), LLM Call (async LLM forward pass) |
| **Guardrails** | Schema Validator (JSON extraction + schema validation), GH Definition Validator (GhJSON/ghpatch parse + library schema + structural integrity), Component Resolver, Required Input Check (statically knowable wiring defects: required inputs wired/internalized, multi-wire into item-access inputs, endpoint paramIndex bounds, orphan data components — full graphs and ghpatch adds), Fidelity Check (post-placement intent-vs-realization diff via the authored-placement ledger; self-sources the definition recorded at placement when its Definition input is unwired/miswired; full graphs only, patches pass through), Runtime Health Check (was Canvas Observation — errors/dead/null scan with sampled values; Fail on Warnings is a context-MENU toggle, not an input — never register an input before the base-appended Signal on a shipped RoutingComponentBase subclass, it shifts saved-doc param layouts), Geometry Observation (viewport snapshot; single Signal output via `HasFailOutput => false`), Geometry Report (text-only spatial digest: per-component bboxes, disjoint groups + gaps, containments — the non-image fidelity feedback; single Signal output via `HasFailOutput => false`. Its closing instruction is single-shot — "matches your intent → reply in prose" — UNLESS the Message input carries a Build Plan progress digest, detected by `BuildPlanParser.DigestMarker`, in which case the digest's staged instruction replaces it and leads the report) |
| **GhPython** | PyTransmitter (pushes generated Python into a linked Script component — linked via its right-click "Link to Script Component" picker over the HOST canvas, since a grip drag cannot cross into a harness; the drag arrow itself is hosted by the harness proxy, on the grip labelled "py"; routes its errors; when an enabled Script I/O grip-links to it, freezes the param SET but still applies hint/access corrections in place **and still runs the Python marshalling repairs** (`ApplyPythonOutputMarshalling` — No Type Hint on outputs + `MarshOutputs` on — on EVERY push: `SetScript` copies that flag off the script, so skipping them under lock reproduces "Data conversion failed from Goo to …" on any list output; they are not interface changes and stay on PyTransmitter, never the shared base). Pushes **code only** in the sense of the param set — never restructures the target's params — and rejects submissions declaring unknown input/output names with corrective Fail feedback), PythonShortcut |
| **Grounding** | ClusterGrounder (.ghx cluster — scaffold), PythonGrounder (python function — scaffold), CanvasStateGrounder, ComponentCatalogGrounder, DocumentUnitsGrounder, Tools Present (`ToolsInUse` — scans Router-wired tool nodes, emits `ToolsGrounding`; lives here, not under LLM Tools, because its output is grounding), Script I/O (`ScriptIO`, **renamed 2026-08-11 from "Interface Lock"** — class/file/attrib renamed with it, ComponentGuid `B7D2F4A9-…0A46` pinned; grip-links to **any `ScriptTransmitterBase`** (Py or C#) via its own bottom arrow/gradient wire, reads that transmitter's target script component and emits `ScriptInterfaceGrounding`: the exact inputs (name/type-hint/access) and outputs (name/access) rendered as verbatim-copyable submission-JSON entries, declared LOCKED, **plus what the canvas DOWNSTREAM of each output already demands** (`GhPythonBridge.GetOutputRecipientTypes` walks each output's `Recipients` and reports their `TypeName`s — so an untyped `wall_out` plugged into a Mesh param tells the model to assign a Mesh). Two traps there: a **Panel reports `TypeName` "Text"** but accepts anything and stringifies it, so panels are excluded or every debugging wire would order the model to stringify geometry; and GH calls an Interval **"Domain"**, the one name that doesn't already match the hint vocabulary. Unknown/`Generic Data` recipients are reported as no constraint rather than guessed at. Symmetrically, `GetInputIncoming` reports the **live data** on each connected input (`VolatileData` count/branches + the goo's own `TypeName`, falling back to the source param) — "2 Curves" — and the grounding states the mismatch when the declaration disagrees ("declared item but 2 items arrive… use list"). Signature keys incoming by **shape** (type / one-vs-many / flat-vs-tree), never exact count, or every slider tick would re-solve the grounding and expire the Conversation Log. **The lock now freezes NAMES only** — the model MAY correct a type hint or access, and `ScriptTransmitterBase.ApplyLockedInterfaceAdjustments` applies both IN PLACE on push (`UpdateConverter` for the hint, the access re-stamp for the mode), so the wires survive; without that a lock is a ratchet that reports the problem and forbids the fix. The grounding wording was changed to match. Both are rendered as PROSE, never as a `type` on the output entry — the schemas set `additionalProperties:false` there, so a copied entry carrying a type would fail validation. Wiring a component to an output expires the downstream and runs a HOST solution, so watch (2) already senses it — but only because the wiring is folded into `CurrentSignature`; the same link makes the transmitter enforce the contract — enforcement (`ActiveInterfaceLock` / `RespectsLockedInterface` / the feedback) lives on `ScriptTransmitterBase`, shared. **A parameter set is language-neutral; the prose about it is not** — what the model is told comes from the transmitter's `ScriptInterfaceDialect` (component kind + schema name + code rule; `ScriptInterfaceDialect.Python` / `.CSharp` in Core), never from a branch in the lock. It has no inputs, so nothing in the pipeline ever expires it — it refreshes off **three** watches, and each one covers a case the others structurally cannot: (1) `SolutionEnd` on the **local** document = the LINK changing (the transmitter is a harness peer, so re-pointing it re-solves here); (2) `SolutionEnd` on the **host** document = adding/removing a param or changing access (the target sits on the user's canvas, so those solve the HOST and leave the harness untouched — one subscription sufficed before the harness split and is a stale-contract bug after it); (3) **`ObjectChanged` on the target component AND each of its params** = a RENAME, which reaches no SolutionEnd anywhere; its `Layout` event additionally re-arms the whole subscription, because a push REPLACES the param objects (`UpdateInput/OutputParameters` rebuild rather than mutate) and would otherwise leave every handler on a discarded param from the first push onward — the component survives that rebuild, so it is the only safe anchor. **Two traps, both tried and both regressed it to detecting nothing: `Layout` must NOT trigger a contract check (it fires mid-rebuild and would sample a half-built interface into the last-emitted signature), and the scheduled callback must stay `ExpireSolution(false)` — GH flushes scheduled delegates at the START of the next solution, so marking expired is exactly enough, while `true` asks for a solution from inside one and is re-entrant. **Why a rename is different:** GH expires along the data graph, and a param's recipients are downstream of it. Renaming an INPUT expires the component (the input's recipient IS the component) → solution → caught by (2). Renaming an OUTPUT expires only what is wired below it — the component is UPSTREAM of its own output — so an *unwired* output rename expires nothing and runs no solution at all. On a script component the param name IS the variable name, so that is a real contract change. Watches (2) and (3) are re-pointed on every solve and every callback, since the harness owner is not always resolvable at `AddedToDocument` time and the param set itself changes. **A change detected this way MARKS the component expired (`ExpireSolution(false)`) and must NOT post a `ScheduleSolution` to the harness sub-document** — the edit happens on the user's canvas, so the host solves and the harness does not; a sub-document is only re-enabled when its proxy solves and a disabled one ignores scheduled solutions, so the callback is dropped and the stale contract reaches the next inference. Expiring is enough: this component is upstream of the Conversation Log, so the solve the user's next prompt causes re-solves it first. **The same trap applies to any harness-resident component reacting to a host-side event.** Enforcement needs none of this — `RespectsLockedInterface` reads the live specs at push time. Disabling the component suspends the lock without unlinking) — all emit `GH_Grounding` for the Conversation Log's Grounding input. `Grounding` is a Core discriminated union (`ComponentCatalogGrounding` migrated from System Prompt's old catalog input; `GH_Grounding.CastFrom` adapts producer goo like `GH_ComponentCatalog`) |
| **LLM Tools** | Model-callable tools (`LlmToolComponentBase : StatefulComponentBase`; **a tool result is TEXT on every provider**, so a tool answering with an image returns it as an ATTACHMENT — `ToolCallResult.OkWith(text, blocks)` → `ToolCallOutcome.Attachments` → `ToolBatchRunner` → `Router` → `ToolDispatchRound.CombineResults` → `ConversationLogBuilder.RecordToolSignal`, riding the SAME answering user turn as a sibling block. **Attachments must sort after every `ToolResultContent`** (Anthropic requires tool_result blocks to lead that turn); the runner and the Router each enforce it. No provider change was needed: Anthropic puts tool_result + image side by side, the OpenAI protocol already splits such a turn into role:tool messages plus a role:user message, Gemini emits functionResponse + inlineData parts, `ToolPairing` only reports, and compaction's `Reassemble` keeps non-tool blocks via its `default` arm. Before this, the Router filtered to `ToolResultContent` and `RecordToolSignal` dropped non-call blocks — an image was silently lost in BOTH places; Core type `LlmToolDefinition`, goo `GH_LlmToolDefinition`, param `Param_LlmToolDefinition`; the base owns Tool(0)/Result(1) and, since 2026-08-17, `RegisterAdditionalOutputs` + `OnSolveEnd` — publish a call-mutated output from `OnSolveEnd`, not `OnSolveTick`, which runs before the calls and leaves the wire a solve behind): WebSearch, ReadUrl, MemoryTool, RhinoGeometryTool, ComponentSearch, RhinoCommonSearch, Take Snapshot (`take_snapshot` — the model LOOKS: a camera stands at the wired `Current Location`, aims anywhere over the full sphere by `azimuth` (0=+Y forwards, 90=+X right, clockwise in plan — the SAME bearing convention as the movement cones, shared structurally via `SpaceNavigator.Aim`/`BearingLabel` so one mental compass covers walking and looking) plus `elevation` (−90..90, clamped not wrapped; azimuth IS wrapped), captures, and hands the image back. 35mm-equivalent lens ≈ 54° horizontal FOV, and the model is TOLD that number so an off-camera thing is not read as an absent thing. Outputs `Current View` (the latest aim on its own, left EMPTY before the first look — a zero vector would read downstream as a real direction) and `Snapshot Directions`, a TREE with one branch per VISIT to a Current Location (revisits open a new branch — branch order stays walk order, which downstream can collapse by point but could never re-split). `RunsAsync => true` NOT for speed but because posing a viewport is illegal inside a solution and must be on the UI thread: the capture is marshalled to `RhinoApp.Idle` and awaited off the solve, with a timeout so a never-idle Rhino cannot hang the round with its tool id unanswered. `ViewportSnapshot.TryCaptureFromCamera` borrows the user's viewport and restores it exactly via Rhino's own `PushViewProjection`/`PopViewProjection` in a `finally`), Move In Space (`move_in_space` — walks the model one step at a time through a user-supplied `Positions` lattice from a `Start Point`, publishing the route on `Traversed Points` plus `Current Position` (where it stands now, on its own so a camera wires straight to it — this is what feeds Take Snapshot's `Current Location`). That geometry is the point of the tool: it turns the model's spatial reasoning into something the definition can build on. Optional `Position Notes` gives each position a description reported to the model whenever it stands there — arrival AND look alike, since the note describes the POSITION; paired with `Positions` by Grasshopper's longest-list rule via the pure `Core/Common/ListPairing.MatchLongest` (equal lengths 1:1, a shorter list reuses its LAST note, surplus notes ignored), done in code because this component reads both as whole lists in one solve and must not iterate. Unwired = no notes. Adjacency is DERIVED, never configured — `SpaceNavigator` buckets each candidate by vertical band (same/up/down by doc tolerance) and by one of eight 45° in-plane cones, then offers only the CLOSEST per bucket, which is what makes a step a step: collinear points further along a bearing arrive one move later, and `up_*` resolves to the next level up rather than the top of the stack, with no level clustering anywhere. Eight cones means full angular coverage, so a rotated or scattered cloud is still fully navigable and nothing reachable is ever hidden. Directions are FIXED WORLD directions — forward=+Y, right=+X, up=+Z — deliberately not heading-relative: a heading is undefined on the first move, must be tracked by the model across turns, and flips left/right as it turns. The 26 tokens are generated from `SpaceNavigator.AllTokens` into the schema enum so advertised and accepted can never drift. `direction` is optional — omitting it reports position + options without moving, which is how the model gets its bearings on the first call, since nothing else tells it where it starts. An unavailable-but-real token is an `IsError` result that re-lists the legal moves, so the model never assumes a move it did not make. Walk state is session-only and restarts when the Start Point moves) — plus Router (dispatch loop). Tools Present lives in the **Grounding** section |
| **Human Tools** | Chat-window affordances for the HUMAN, not the model (`HumanToolComponentBase : PhyBase` — passive emitters: no inputs, one `Param_HumanTool` output; Core union `HumanTool` in `Physalia.Core/HumanTools/`): Geometry Snapshot + View Snapshot (both `SnapshotToolComponentBase`, which owns the shared "Send With Default Message" context-menu toggle: on = the capture is sent as its own message carrying an editable default message, off = it attaches to the prompt box for the human to caption, on its OWN image lane independent of Add Image and of the other snapshot tool. Geometry Snapshot frames the camera on transmitter-generated geometry and is armed only while such geometry exists; **View Snapshot captures the active viewport as-is — no geometry scan, no camera move, so wired is armed**), Add Image (enables image paste/drop/picker in the prompt box — image intake is fully disabled without it, except for a snapshot tool's own attach lane, see `ConversationLog.AcceptsPromptImages`), Export Conversation (header button → saves the viewed conversation as a .txt transcript; **replaced the `/export` slash command**, which no longer exists — the composer now has no built-in commands), Signal Trace (header button → opens `SignalTraceWindow`; **replaced the signal-trace canvas widget**, which was deleted. The trace log itself is still process-wide/session-wide, not per-conversation). Wired into the Conversation Log's Human Tools input; never touch the system prompt, never advertised to the model |
| **Models** | AnthropicModel/Tweaker, GeminiModel/Tweaker, OpenAICompatibleModel/Tweaker, ModelInformation, LlamaCppModelInfo, ApiKeys (+ `ModelComponentBase`, `TweakerComponentBase<TConfig>`), plus the two **local-CLI** models that take no API key and derive from `PhyBase` directly: ClaudeCodeModel, CodexModel (Model + Effort, both Picker-backed; its model list is fetched live from the CLI) |
| **Control Flow** | Feedback, FeedbackCollector (wireless signal transport via grip-link; deliberately breaks the GH DAG), Detect JSON (presence gate — single Signal output via `HasFailOutput => false`; attempted JSON, even malformed, passes through; plain conversation dead-ends quietly inside the component via `RoutingResult.Fail(emitSignal: false)`), Build Plan (staged generation: parses the model's `<plan>` block out of each response and renders a progress digest on a `Progress` text output for the Geometry Report's Message input — a pass-through tap, never a gate; see `planning/incremental-building.md`), Signal Limiter (caps total loop rounds), Merge Signal (joins two or more signal branches into one — variable inputs via the zoom +/- icons, minimum two, added/removed at the END only because the hold and the base's consume-once marks are index-keyed. A **join, not a passthrough**: parallel branches latch on their own scheduled solves, so emitting per solve would give one signal — and one logged turn — per branch; it holds the newest signal per wired input and mints ONE merged signal once the whole wired set is in. Merge order is global sequence (causal) order: payloads blank-line-joined, ContentBlocks combined, newest Instructions kept, outcome Failure if any part failed. **Combining blocks is not concatenation, and every aggregator (this and the Feedback Collector) must use `SignalAggregation.Combine`:** a signal's ContentBlocks are the WHOLE turn and its Payload only their text trace, so joining payload strings while merely concatenating block lists leaves a text-only branch's text in the payload and in no block — the Conversation Log then records the blocks and silently drops the text (a merged Geometry Report + Geometry Observation reached the model as the image alone, 2026-08-17). A branch with no blocks of its own therefore contributes its payload AS a TextContent block; blocks are materialised only when some branch carried them, so an all-text merge stays text-only. Unwired inputs are ignored; a round where a wired branch never fires parks at `1 / 2` until it does — `Clear Outputs` abandons it), Stall Guard (caps *identical* failure rounds — fingerprints failure payloads; escalates at the Stall Limit, suppresses re-emission beyond it; Stall Limit is input 0, single Success Signal output — parked loop = STALLED caption only, nothing emitted) |
| **Serializers** | Serializer / Deserializer (.ghjson canvas export/import via `GhJsonBridge`), SchemaTranslator |
| **Signals** | ConstructSignal (manual mint), DeconstructSignal (passive inspect — never consumes) |
| **Receivers** | Receiver (`Rx`, the harness's inlets — the inverse of a transmitter and the only thing crossing the boundary as a wire. No inputs, one generic tree output; placing one inside a harness grows an input on the LEFT edge of the proxy, named after this node's nickname, and whatever is wired in out there arrives here tree-intact. Generic rather than geometry-typed on purpose: geometry is the main cargo and rides through untouched, but a goal condition is usually stated partly in numbers and text. **Passive — it mints NO signal and starts no round**: it latches what it was handed and re-emits it on every solve of the harness, so the value is current whenever a signal-driven round reads it. That is what makes it safe to feed a harness from a slider, and it is what stops transmitter-writes-canvas / canvas-feeds-receiver closing into a cycle GH's own detector cannot see — nothing in the pipeline ACTS on inlet data by itself. Outside a harness it has no proxy to grow an input on and says so) |
| **Transmitters** | The harness's outlets — everything that writes OUT of it (`TransmitterComponentBase`, see the Harness section): Component Transmitter (`CompTx`, grip "node" — places/patches a GhJSON graph on the canvas), Text Transmitter (`TextTx`, grip "text" — **the odd one out: NOT a `TransmitterComponentBase`.** One generic `Data In` (a signal OR text), one generic `Data Out` — a plain passthrough with no routing, no latching, no state machine: a signal leaves as the SAME signal (same sequence, so consume-once downstream still holds). What it transmits is the text form of whatever arrived — a signal's payload, or the text. Its grip connects like a **standard GH output**: drop it on an input GRIP and it links that input — ANY input, not just text ones, since delivery goes through `SetPersistentData(params object[])`, which casts the value into whatever the param holds exactly as a wire's data would be. Target resolution mirrors GH's own: nearest input grip (12u), else the row under the cursor, else the node's first input; a Panel or floating param links directly. **A drop on empty canvas does nothing — it never creates a target.** Delivery is deferred to `RhinoApp.Idle` — it writes into and expires the HOST document from inside a harness solve, which cannot be done in-solution — and is keyed so it fires once per change (signals by sequence, text by value). A target with a wire into it warns, since internalized data loses to a wire), Geometry Transmitter (`GeoTx`, grip "geo" — the geometry counterpart of Text Transmitter and built the same way: NOT a `TransmitterComponentBase`, one `Geometry In` / one `Geometry Out`, plain passthrough, no routing/latching/state machine. `Param_Geometry` on both sides (so every geometric kind rides through and the viewport previews it; **Vector/Transform/Interval are NOT `IGH_GeometricGoo`** and do not) with `GH_ParamAccess.tree` — **the tree is data**: it passes out unchanged AND is internalized into the target branch-for-branch. That last part needs more than `SetPersistentData(params object[])`, which can only make ONE flat branch: `ParamTargets.WriteTree` walks the target's base chain to its constructed `GH_PersistentParam<T>`, builds a `GH_Structure<T>` by reflection, casts each item with the param's own protected `Cast_Object` (the very conversion the flat setter performs, so a Brep entering a Mesh input converts exactly as through a wire), and calls the structure-taking `SetPersistentData` overload; items the param cannot read are COUNTED and reported, never silently dropped. **Stringifying is PER ITEM and keeps the container**: `Param_String.Cast_Object` already turns each goo into text on its own (verified — a `GH_Point` casts to `"{1, 2, 3}"`), so a text param takes the tree intact through the normal path; on top of that, an item any param refuses outright is offered again as a `GH_String` of its text form (a param that reads neither refuses both and is counted, so the fallback only fires where it is right). A **Panel** is the one target that cannot hold data — a `GH_Param` but NOT a `GH_PersistentParam`, its only storage is one `_userText` string — so `ParamTargets.WritePanel` writes **one item per line** and forces `Properties.Multiline` off, because `CollectVolatileData_Custom` splits that string BY LINE into one branch: a LIST round-trips exactly, each piece cast to text on its own. **A Panel parses no paths back** (probed: `{0;0}` headers come back as data ITEMS, so rendering GH's own tree display into it would corrupt the data), so a multi-branch tree is flattened and the component warns and points at a Text parameter. A newline inside an item's own text is collapsed to a space, since line count IS item count there. `CanHoldOrDisplay` is the shared "Panel or persistent param" target test both wire-like transmitters use. Change-detection is by tree shape + **reference identity** of every goo (`RuntimeHelpers.GetHashCode`) — GH re-mints goo only when its producer recomputes, so this can re-send unchanged-looking geometry but can never MISS a change, where a bounding-box compare would fail the other way. The queued tree is COPIED (`new GH_Structure<T>(tree, false)`), since the one from the solve belongs to the input param and is cleared before the idle callback runs. Everything else — target resolution, the deferred `RhinoApp.Idle` write, the wire-into-target warning — is shared with Text Transmitter via `ParamTargets`. **No icon yet: falls back to `brain.png`**), C# Transmitter (`CsTx`, grip "C#" — pushes an LLM-generated `CSharpComponent` submission into a linked **Rhino 8 C# Script** component. Same JSON shape as Python (`ScriptComponentJson` parses both), but C# declares its parameters TWICE — in the submission and in the `RunScript` signature the engine reads out of the source — so the push is gated on a signature check that rejects a disagreeing submission before anything reaches the canvas and spells out the expected signature in the Fail feedback. Lockable by a Script I/O like the Python one — the two checks compose (lock pins declared params to the target's, signature pins the code to the declared params) — except a locked C# submission must declare the interface WHOLE (`AllowsPartialInterface => false`): an undeclared param has nothing in the signature to bind to, where Python just never mentions the variable. None of PyTransmitter's marshalling repairs: they exist for the Python engine's value wrapping. System prompt pair: `PREAMBLE/C# Script.txt` + `SCHEMA/C# Script.json`), PyTransmitter (listed under GhPython) |
| **Tokens** | TokenEstimator |
| **Utility** | Picker, Conversation/Message/Instructions Compositors + Decompositors |

### Planned, not yet built (spec: `planning/physalia-primitives.md`)
PyValidator, Counter, Meter, Monitor, Aggregator — note that doc's "Receiver" is a **different, superseded** thing (the galapagos-wired build target that became the transmitters), not the harness inlet built in 2026-08-18 — plus LLM Call alternate roles via `.skill` files (Distiller, Reflector, Interpreter, etc.).

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
