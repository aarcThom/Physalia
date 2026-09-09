# Condensed sections — the previous full text

> These sections were CONDENSED (not moved) when `CLAUDE.md` was split on 2026-09-08. The wording below is the original, kept because the summaries in CLAUDE.md drop clauses that were earned live.

## Signals & Component Lifecycle — original wording

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


## Settings live on the component they configure — original wording

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


## C# conventions, GH patterns, async, build — original wording

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
        (no key file — credentials live encrypted in %LOCALAPPDATA%/Physalia/credentials.dat,
         written only by the chat window's setup page)
        (no MCP file — servers live in %LOCALAPPDATA%/Physalia/mcp-servers.json,
         written only by the chat window's MCP page)
        (no API file — endpoints live in %LOCALAPPDATA%/Physalia/api-endpoints.json,
         written only by the chat window's API page; their keys sit in credentials.dat)
        /SYSTEM_PROMPTS   ← /PREAMBLE + /SCHEMA, resolved by name from the System Prompt component
        /CLUSTERS         ← .ghcluster files + clusters.json manifest (Cluster Grounding)
        /PRESETS          ← preset harnesses (.phy — a zip of manifest + harness.gh + files/;
                             plain .gh still read)
            /Physalia     ← shipped with the plug-in
            /User         ← written by "Save Harness as Preset…"
            /Community    ← reserved, not populated yet
        /MEMORIES         ← memory tool: /GLOBAL and /LOCAL/<Memory Folder input, or the node's id>
        /PROJECT_FILES    ← one folder per harness, named after it: downloads, site data, /PDF
                             conversation.json + /conversation-images (autosaved transcript),
                             runs.jsonl (one line per inference call)
                             (Files/PDFS is GONE — PDFs are project material and live inside the project)
```
