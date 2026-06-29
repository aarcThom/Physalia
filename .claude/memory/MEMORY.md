# Physalia Project Memory

## Meta
- [Memory sync setup](memory-sync-setup.md) — CLAUDE.md + this memory sync across computers via git: canonical files in repo `.claude/memory/`, global memory path is a junction into the repo. New machine needs a one-time junction (command in the note).

## General Preferences
- Search the internet (WebFetch, WebSearch) anytime it would help answer a question — don't hesitate.
- Record progress in MEMORY.md (and topic files if needed) whenever meaningful progress is made — don't wait to be asked.
- Make code changes only when explicitly prompted (e.g. "make this change", "edit this", "fix this"). Default to advice/answers only otherwise.
- Never run git commit. When asked for a commit message, write it out as text only.
- [Commit/PR messages output-only](commit-and-pr-messages-output-only.md) — when asked for a commit message or PR description, print it in chat only; never run `git commit`/`push`/`gh` — the user runs the git action themselves (holds even when they say "we'll just commit to main").

## Refactoring
- [Tier-1 refactoring](tier1-refactoring.md) — done in working tree 2026-06-29 (not committed): new `src/Physalia.Core.Tests` (xUnit, 96 green), provider stream fixtures, token-estimator interface split (ISync/IAsync markers, no runtime-throw), and pure-policy extractions ConversationRecorder/ToolDispatchRound/ToolBatchRunner. GH components still need a Rhino sanity run. Tier-2 (assembly split, DTOs, ChatWindow/GhJsonBridge decomposition) deferred. Plan in `planning/refactoring.md`.

## Project
- Grasshopper AI plugin for Rhino (Physalia). Pair-programmer role + working dir: see CLAUDE.md.
- [Chat widget](chat-widget.md) — bottom-right GH canvas widget (above the compass) that opens the chat window; find-or-creates a Chatbox; setup-state detection via IsPipelineReady. Windows-only for now.
- [Chat window](chat-window.md) — standalone Eto WebView chat (additive to Prompter). FULL Svelte/shadcn/svelte-ai-elements UI in new `src/Physalia.UI` project (chain-of-thought thinking, tool calls, light mode, image paste/drop, JSON auto-collapse); bundle embedded in the `Physalia.GH` assembly (not `Files/`), extracted to a temp file at runtime (2026-06-29). Send/freeze/scroll/JSON fixed in Rhino; broader live test pending. Plan: `planning/chat-window.md`.
- [UI design: neumorphism](ui-design-neumorphism.md) — chat UI design language (soft UI: one blue base, dual shadows, no borders); `--neu-*` tokens + `.neu-*` helpers in app.css, plus the two edge-shadow gotchas (gutter clip, overflow-hidden clips child shadows).
- [Chatbox switcher row](chatbox-switcher-row.md) — bottom row of circles in the chat window switches the single window between multiple Chatbox components (`ChatWindow._component` now mutable + `SetActiveComponent`); `selectchatbox` bridge verb; double-click another Chatbox switches view. 2026-06-23, builds clean, live test pending.
- [Preset placement](preset-placement.md) — "Add preset" splices the live Chatbox into the preset's placeholder Chatbox slot via `GhJsonBridge.LoadAndPlaceAnchored` (no duplicate); + `ExpandToFullName` ExpireLayout fix (full param names on GhJSON-placed components) + chat window centres over the GH editor on multi-monitor. 2026-06-23, builds clean, live test pending.
- [Chatbox emoji identity](chatbox-emoji-identity.md) — each Chatbox gets a random ocean emoji as its canvas icon + switcher dot (TextRenderer color emoji, deduped on placement, persisted). 2026-06-24, builds clean, live test pending.
- [GH no-preview Hidden palette](gh-nopreview-hidden-palette.md) — to tint a signal-only GH component's capsule via GH_Skin, swap the Hidden palette (not just Normal): GH forces non-preview-capable nodes onto GH_Palette.Hidden. (Chatbox ultimately custom-renders instead.)
- [GH collapsed-harness grips](gh-collapsed-harness-grips.md) — block wire-drag from a collapsed harness by gating param-attribute HasInputGrip/HasOutputGrip (RelevantObjectAtPoint), not the component; hidden members leak grips at the proxy pivot. HarnessParamAttributes + Harness Hide/ShowMember.
- [Collapsed Chatbox arrow](collapsed-chatbox-arrow.md) — collapsed harness proxy shows a delegated bottom drag arrow (new `IHarnessArrow`) when it holds exactly one transmitter; grip hit zone bottom-centre only so the proxy stays movable. 2026-06-25, builds clean, live test pending.
- [Collapsible harness](collapsible-harness.md) — hide/show a Chatbox's pipeline behind the single Chatbox proxy node (in-place visual collapse, not a real cluster); new `src/Physalia.GH/Harness/` folder + `Harness` class, `PhyBase` collapse flag, native-member attribute-swap, chevron + menu + chat-window toggle. 2026-06-24, builds clean, live test pending.
- [Arrow DRY refactor](arrow-dry-refactor.md) — drag-arrow grip/wire-cache/drag-state-machine unified into one `ArrowGrip` controller + `IArrowHost` (composition, so ChatboxAttrib shares it); pluggable `IArrowHead`/`TriangleArrowHead`; central `ArrowStyles` gradient palette; `CollapsedProxyAttributes` moved to `Attributes/`. 2026-06-25, builds clean, live test pending.

## Platform / Build
- Two projects only: Physalia.Core (net7.0) and Physalia.GH (net7.0-windows on Windows, net7.0 on Mac — set via OS-conditional TargetFrameworks).
- CLAUDE.md was rewritten 2026-06-11 to match actual code (signal lifecycle, real namespaces/providers, built-vs-planned component inventory, Sandbox removed). If CLAUDE.md and code disagree, trust code, but drift should now be rare.
- System.Drawing warnings (CA1416) are false positives — Rhino ships its own compatibility layer. Suppress with <NoWarn>$(NoWarn);CA1416</NoWarn> or #pragma warning disable CA1416.
- [Physalia repo gotchas](physalia-repo-gotchas.md) — slnx lives in `src/` (`dotnet build src/Physalia.slnx`), primitives doc in `planning/`; Files-folder build pipeline (repo-root `Files` → bin via `CopyLibraryFiles`) + its two MSBuild gotchas: the stray `src/Physalia.GH/Files` (empty `$(TargetDir)` in the outer multi-TFM build — now guarded + duplicate removed from git) and the VS one-build UI lag (BOTH `Physalia.UI` and `Physalia.GH` need `DisableFastUpToDateCheck`).

## Tool Calling (robustness Phase 4)
- [Phase 4 keystone](tool-calling-phase4.md) — provider contract now SENDS tool definitions (`ToolDefinition` + `StreamAsync` gained `IReadOnlyList<ToolDefinition>? tools`); GH visible-loop still TODO. Landed 2026-06-16.
- [GH tool-calling loop](tool-calling-gh-loop.md) — Reasoner/Router/tool nodes; Router aggregates results per round; tool nodes inherit `ToolComponentBase` (owns multi-call contract: ExecuteCall per call, one ToolResultContent per call). Landed 2026-06-17.
- [Tools In Use component](tools-in-use-component.md) — new GH node scans doc for tool nodes wired to a Router, emits their definitions as one list into Reasoner.Tools (replaces manual per-tool fan-in); + public `ToolComponentBase.AdvertisedDefinition`. Builds clean, live test pending. 2026-06-25.

## Claude Code provider (warm process)
- [ClaudeCode warm-process rework](claudecode-warm-process.md) — provider now keeps ONE `claude` CLI process warm per Reasoner (stream-json in/out) instead of cold-starting per call; SDK is a dead end (no .NET, wraps the CLI, needs API key). Builds clean; live-Rhino timing/leak check still TODO. Landed 2026-06-18.
- [ClaudeCode provider perf](claudecode-provider-perf.md) — the "freezes on real prompts" was extended thinking (fix `MAX_THINKING_TOKENS=0`); warm session ≈ API parity, cold start is native-binary-bound (not flag-bound); `--safe-mode` keeps OAuth, `--bare` breaks it; pipes pinned to no-BOM UTF-8. Measured 2026-06-18.

## Compaction
- [Conversation compaction](conversation-compaction.md) — sliding/token/anchored window + content prune + LLM summarizer; Core `Physalia.Core/Compaction/` (Reassemble keystone) + GH **Compaction** tab. **Reworked 2026-06-27: Instructions ride the signal; inline forward-path `Recorder → Compactor → Reasoner` (Recorder = signal-only, uncompacted source of truth).** Report: `planning/conversation-compaction.md`. Builds clean.
- [Signal carrier discipline](signal-carrier-discipline.md) — PhySignal carries exactly Payload + ContentBlocks + Instructions; never add carrier fields for arbitrary types (god-object guard). In CLAUDE.md too.

## Pickers / Models / Prompts (2026-06-27 batch)
- [Picker GhJSON serialization](picker-ghjson-serialization.md) — Picker selection now round-trips through `.ghjson` via `physalia.pickerValue` extension (native `.gh` already worked); stores only labels, never API-key secrets.
- [Model Information + minified prompts](model-information-and-minified-prompts.md) — ModelInformation now merges OpenRouter+LiteLLM with id normalization; minified preambles/schemas in `Files/SYSTEM_PROMPTS/` for small models. Research docs: `planning/deterministic-gates.md`, `planning/tool-components.md`, `planning/model-information.md`.
- [Web search tools](web-search-tools.md) — `web_search` (Tavily) + `read_url` (Jina Reader, keyless) tool components; Core `Physalia.Core/Web/WebTools.cs`; keys via new `web_search` YAML section. Tools block on async HTTP (ToolComponentBase is sync). Research: `planning/web-tools.md`. 2026-06-27, builds clean.
- [Python output list access](python-output-list-access.md) — LLM Python outputs wrap lists as one unreadable goo. Root cause (2026-06-29): RhinoCode forces `AutoDeclare=!HasInstance`→Item on first push of fresh scripts. Tried Any-type (killed fatal crash), alias inference, in-place `ParamsApply` re-apply — STILL BROKEN. Next: force true "No Type Hint" converter + diagnostic read-back.

## v2 Architecture
Full Core architecture decisions locked 2026-05-03. See [v2-core-architecture.md](v2-core-architecture.md).
Component-level spec: planning/physalia-primitives.md. API research: planning/api_research.md.

## Signal Lifecycle (rework landed 2026-06-10/11; replaces ALL trigger/pulse designs)
Bool triggers, momentary pulses, SHA-256 change detection, and Data/Feedback output ports are **gone** (commits 91c83c5, 93ee097, d6a086c). Events are latched, sequence-numbered, consume-once `PhySignal`s (Core/Signals); the payload is the only data carrier between pipeline components (Success Signal(0) / Fail Signal(1), one wire per hop). Two-layer bases: `StatefulComponentBase` (state machine, ObserveSignalInputs/Consume*/Latch*, wall-clock-honest `ScheduleStateSolve` funnel) → `RoutingComponentBase<TData>` (push/read/latch; async = `AutoScheduleRead=false` + `RequestReadPass()`). Nothing in the lifecycle serializes — components reopen Empty.
**Authoritative doc: `planning/data-marshalling.md` in the repo** — read that, not memory. Non-repo leftovers: [routing-trigger-system.md](routing-trigger-system.md).
- [Trigger state machine status](trigger-state-machine-status.md) — marshalling history (bool pulses → PhySignal; GH keeps ONE schedule timer, shorter delays replace longer); the **locked decisions Thomas said not to relitigate** + manual-Rhino-verification-still-pending status (2026-06-11).
- **Multimodal extension (2026-06-13):** `PhySignal` now also has an optional `IReadOnlyList<MessageContent> ContentBlocks` (init prop, default empty; `Mint` takes an optional `contentBlocks` arg; `StatefulComponentBase.LatchSuccess` forwards it). The string `Payload` stays the text/trace carrier; `ContentBlocks` rides alongside when a turn is richer than text. Needed because the only wire from Prompter→Recorder is the signal — an assembled image+text user turn can't use a parallel data wire. So "payload is the only carrier" is now "payload is the carrier for text-only events; ContentBlocks for multimodal." This couples `Physalia.Core.Signals`→`ConvoInstruct` (MessageContent). Only Prompter mints with blocks today; feedback/response/Construct-Signal mint empty.

## Prompter image references "/<alias>" (2026-06-13)
Prompter gained input 0 = **Image Sources** (`Param_ImageSource`, list, optional) fed by Image Gatherer. Typing `/<alias>` in a prompt and submitting (Shift+Enter) resolves each token to the referenced image **inline** (text split around the token; token text removed) → interleaved `TextContent`/`ImageContent` blocks delivered to the model.
- Pure parser in Core: `Physalia.Core/ConvoInstruct/PromptImageResolver.Resolve(prompt, IReadOnlyDictionary<string,ImageSource>) → ResolvedPrompt(Text, Blocks)`. `/` matches only at a word boundary (so URLs/`and/or`/paths are safe); known aliases matched longest-first, case-insensitive, requiring a non-word boundary after the alias; unknown `/x` stays literal. `Text` (token-stripped) = signal payload.
- Flow: Prompter caches alias→source map each `SolveInstance` (submit fires from UI, outside solve) → `LatchSuccess(text, contentBlocks: blocks)` → Recorder's `ApplySignal` prompt case records `ContentBlocks` (via new `RecordUserBlocks` + `Conversation.MergeIntoLastUserMessage(IReadOnlyList<MessageContent>)` overload) when present, else the old text path. Images-only prompt (blank text, has blocks) records fine.
- **Aliases are single-token (no whitespace)** so `/<alias>` is unambiguous: `ImageGatherer.SanitizeAlias` (whitespace→`-`) sanitizes defaults; the Manage Images panel rejects whitespace on alias edit. Provider adapters already serialize `InlineImage`, so no provider work.
- **PrompterAttrib gotcha:** `PrompterAttrib` fully overrides `Layout()`/`Render()` (custom panel) and renders the Objects channel itself, so GH does NOT auto-draw or auto-position param grips. Each param needs a manual `LayoutXParam()` (set `param.Attributes.Pivot` + `Bounds`) AND a custom `DrawWireGrip` call, or it's invisible/unwireable. The Image Sources input grip mirrors the output grip on the LEFT edge of the convo panel, vertically aligned (same midY); `_layoutBounds` is expanded on both sides (`x-4, _width+8`) so both grips stay clickable.

## DRY Refactor (2026-06-10) — shared base classes
Codebase-wide dedup; all GUIDs/param names/serialization keys preserved. New shared infrastructure:
- **Core:** `Providers\ProtocolProviderBase` (HttpClient, TryGetConfig<T> guard, SendStreamingRequestAsync, SendForStringAsync, ReadStreamLineAsync, ParseModelIdsFromDataArray) — all three protocol providers rebase on it; wire-format parsing stays per-provider. `Common\HttpErrorMapper.MapStatusCode` is the single status→LlmErrorKind source (also used by AsyncTokenEstimation, LlamaCppServerQuery, ModelList). `Tokens\TokenEstimationHelpers` (overhead constants + ExtractText). `Validation\JsonExtractor` (ExtractJson/PrettyPrint moved out of Auditor).
- **GH:** `Components\Models\ModelComponentBase` (Anthropic/Gemini Model — NOT OpenAICompatibleModel, which is structurally different: auto-detect first model, no Picker). `Components\Models\TweakerComponentBase<TConfig>` (all three Tweakers). `Goo\PhyGoo<TGoo,T>` + `Parameters\PhyParam<TGoo>` (all 6 Goo + 6 Param classes). `Attributes\GripLinkAttrib` (drag-to-link grip state machine; FeedbackAttrib multi-link/bottom-anchor, PyTransmitterAttrib single-link/top-anchor).
- **Deleted dead code:** `PythonTest.cs` + `PythonTestAttrib.cs` (superseded by PyTransmitter).
- Tweaker/Model nicknames unified to uppercase (t/p/k → T/P/K) per convention. llama.cpp count_tokens non-success now maps status codes (was always Network).

## API Key goo (2026-06-13)
- [GH_ApiKey goo](gh-apikey-goo.md) — API keys flow as a typed label-only goo (never serialized), not plain text; consumers (ApiKeys/ModelComponentBase/OpenAICompatibleModel) all switched to Param_ApiKey.

## GhJSON (canvas import/export)
GH export/import uses ghjson-dotnet (GhJSON.Core + GhJSON.Grasshopper, both v1.0.0), now referenced in Physalia.GH.csproj. Full implementation guide at src/planning/ghjson-implementation.md (facade API, component designs). Replaces the abandoned Assemblies subsystem.
- [Component Transmitter (CompTx)](component-transmitter.md) — places an LLM GhJSON graph on canvas, routes placed-component errors + orphan-wiring back on Fail (whole-graph analog of PyTransmitter); GhJsonBridge gained `LoadAndPlaceJson` + `PlaceResult.PlacedGuids` (2026-06-13).
- [GhJSON feedback links + comment](ghjson-feedback-links.md) — Feedback→FeedbackCollector wireless links round-trip via component-id `extensions` + `PutResult.IdToGuidMapping` remap; optional Serializer "Comment" input → `metadata.description`. No library change (2026-06-22).
- [System-prompt preambles](system-prompt-preambles.md) — Composer assembles PREAMBLE + SCHEMA from Files/SYSTEM_PROMPTS (only .txt/.json/.yaml resolve, NOT .md); GhJSONSchema.json + `Python3 Script.txt` / `Node Graph.txt` preambles (2026-06-13).
- GhJSON.Grasshopper 1.0.0 requires Grasshopper/RhinoCommon = 8.24.25281.15001. The csproj `Grasshopper` PackageReference was bumped from 8.0.23304.9001 → 8.24.25281.15001 to resolve an NU1107 RhinoCommon conflict. Both stay `ExcludeAssets="runtime"` (compile-time only; runtime uses installed Rhino — dev machine has 8.31, so a benign MSB3277 GH_IO 8.24-vs-8.31 warning appears). GhJSON DLLs + Newtonsoft.Json copy next to the .gha (NOT host-provided).
- WriteOptions lives in `GhJSON.Core.Serialization` (not `GhJSON.Core`). Indented defaults to true.

### GhJsonBridge (façade) + placement nuggets
All GhJSON library calls funnel through `internal static class GhJsonBridge` — now at **`Physalia.GH/Generation/GhJsonBridge.cs`, namespace `Physalia.GH.Generation`** (moved from the old `GhJSON` folder; CLAUDE.md/code authoritative for its current API + `PlaceResult` shape, which has grown beyond the old 5-field record). Anchored preset placement details: [[preset-placement]].
- **NickName round-trip:** GhJSON stores `nickName` only when `!= Name`; export `StripNickNames` nulls them so files carry only full `parameterName`. Import `ComponentHelpers.ApplyNickNameDisplay(PutResult.PlacedObjects)` is **setting-aware** — sets `NickName = Name` + `ExpireLayout` (see [[preset-placement]]) only when `Grasshopper.CentralSettings.CanvasFullNames` is on, else leaves abbreviations (a later toggle is handled by GH's own doc-wide conversion). Applied at ALL Physalia programmatic placements (GhJSON import, ChatWidget's Chatbox [[chat-widget]], `PickerAdd`, `PythonShortcut`). Router's dynamic "T1" nicknames are functional labels — NOT touched.
- **Placer pattern:** `GhJsonGrasshopper.Put` mutates the live doc AND calls `NewSolution(true)` internally — defer via one-shot `Rhino.RhinoApp.Idle`. Place beside a component via explicit `PutOptions.Offset` + `AutoOffset=false`.
- **Serializer** (`Components/Serializers/`) interactive export is Windows-only (`#if WINDOWS`, Mac Todo); **Deserializer** is cross-platform. Both delegate to GhJsonBridge.

## Resources Tab & Image Gatherer (2026-06-12)
New **"Resources"** GH tab (created simply by passing `"Resources"` as the PhyBase `subCategory`). First component: **Image Gatherer** (`Components/Resources/ImageGatherer.cs`) — no inputs, single list output of a new `GH_ImageSource` goo. Right-click → **Manage Images** opens `ManageImagesDialog` (Eto panel) with a GridView (path / editable alias / preview / red-✕ remove) + Add Image / Paste buttons.
- **Alias carried as real data**, not just a display string: new Core record `ImageResource(string Alias, ImageSource Source)` in `Physalia.Core/ConvoInstruct/ImageResource.cs`. Goo `GH_ImageSource : PhyGoo<GH_ImageSource, ImageResource>` (TypeName "Image Source"); param `Param_ImageSource`. Images become `InlineImage(bytes, mime)`.
- **Persistence = file paths + aliases only** (component-level `Write`/`Read`; bytes re-read from disk on load via `File.ReadAllBytes`). Clipboard-pasted images have `FilePath = null` → NOT persisted; missing files on reopen → deferred warning surfaced in next `SolveInstance`. Goo `Write`/`Read` are no-ops (component owns persistence).
- MIME map + unique-alias helpers are `internal static` on `ImageGatherer`, reused by the dialog. Alias uniqueness validated case-insensitively on cell-edit commit (revert + MessageBox on blank/dup).
- **Eto/WPF GridView edit-commit gotcha (cost two crashes to find):** Rhino-Windows Eto is `Eto.Wpf`, so `GridView` wraps a WPF `DataGrid`. (1) Doing grid work synchronously inside the `CellEdited` handler re-enters the grid mid-commit and crashes — defer via `Application.Instance.AsyncInvoke`. (2) Even deferred, the row is STILL in a WPF `EditItem` transaction, and `GridView.ReloadData()` calls `CollectionView.Refresh()` → `InvalidOperationException: 'Refresh' is not allowed during an AddNew or EditItem transaction`. Fix: NEVER call `ReloadData` to reflect an edited cell — make the row model implement `INotifyPropertyChanged` and raise it on the edited property; Eto's property binding refreshes just that cell with no collection Refresh. `ImageEntry` does this for `Alias`.
- **FIRST Eto.Forms usage in the repo.** Referenced via HintPath to Rhino's shipped `Eto.dll` (2.11) with `Private=False` (NOT a NuGet PackageReference) — deliberately matches the runtime to dodge the documented Eto 2.7-vs-2.11 CS1705 conflict (see GH Code Editor note below). Builds clean on Windows. `Eto.dll` holds both `Eto.Forms` and `Eto.Drawing`. Note: Eto 2.11 `GridView.ReloadData()` has NO parameterless overload — pass `Enumerable.Range(0, rowCount)`. Mac `Eto.dll` HintPath is a guess (Mac Todo); Eto UI surface untested on Mac.

## GH Code Editor Investigation (concluded — abandoned)
- Goal was to open the native GH Script editor from PyReceiver double-click
- RhinoCodeEditor.dll uses Eto 2.11.x; Grasshopper NuGet ships Eto 2.7.x → CS1705 hard error at compile time
- Open(3-param) + AddCode(Uri) works but opens Rhino editor, not GH editor with inputs/outputs panel
- Root cause: GH dashboard requires a persistent Grasshopper1Script registered over the component's full lifecycle — not achievable per double-click
- Decision: use custom ScriptEditorDialog (Eto.Forms) instead

## GH / C# conventions, rendering, async, tooling, type hints
**All in CLAUDE.md** — param naming (Title Case, acronyms all-caps, uppercase nicknames), GH rendering patterns (GH_FontServer, custom Layout, AddOutputGrip, ContextMenuStrip, MidY, InstanceGuid vs ComponentGuid), GH async pattern (AddRuntimeMessage main-thread-only → field + emit in SolveInstance), StyleCop/SA1101/copyright header, C# conventions (abstract base over interface, ThrowIfNull, template method, HttpClient on base, abstract const props, XML doc style), SystemPrompt type hints. Read CLAUDE.md, not here.
- **Unique nugget (not in CLAUDE.md):** a persistent on-canvas HUD during an interaction (e.g. Serializer's "select objects then Enter" banner) draws via `GH_Canvas.CanvasPostPaintWidgets += …` (subscribe on interaction start, unsubscribe + `canvas.Refresh()` on end). It paints UNDER the pan/zoom transform — to pin to a window corner, save `g.Transform`, `g.ResetTransform()`, draw in device px, then restore (dispose the saved Matrix). Without the reset the banner lands far out in world space.

## Mac Todo

### PrompterAttrib — WinForms surface untested on Mac
`Attributes/PrompterAttrib.cs` (added 2026-06-11, port of main-branch ComposerAttrib) compiles unguarded on both TFMs like the other attribs, but its WinForms surface — in-place `TextBox` overlay added to `GH_Canvas.Controls`, `System.Windows.Forms.Timer` (busy animation), `Keys`/`KeyEventArgs` in the Shift+Enter handler — has never run on Mac Rhino. Verify the overlay focus/Leave behaviour there; fall back to an Eto dialog if the canvas-hosted TextBox misbehaves.

### Serializers folder — Windows/Mac compatibility split
The two components in `Components/Serializers/` have OPPOSITE cross-platform status:
- **`Serializer.cs` — Windows-only (`#if WINDOWS`).** Interactive export (select objects → Enter/Esc → SaveFileDialog → .ghjson) uses `System.Windows.Forms.SaveFileDialog`, `Keys`, and `KeyEventArgs`, plus a `GH_Canvas.KeyDown` hook. WinForms is only available on the `net7.0-windows` TFM, so the **entire file is wrapped in `#if WINDOWS`** — the Mac (`net7.0`) build compiles fine but the component is simply absent there. The `WINDOWS` symbol is defined explicitly in the windows-TFM `<PropertyGroup>` in `Physalia.GH.csproj` (alongside `UseWindowsForms`), not relied on from the SDK implicit define. **This is the only Serializers component that needs Mac work.** To port: replace `SaveFileDialog` with `Eto.Forms.SaveFileDialog`, replace the canvas `KeyDown` hook with an Eto-compatible keyboard equivalent, then drop the `#if WINDOWS` guard. The HUD overlay (`CanvasPostPaintWidgets` + System.Drawing) and GhJSON export calls are already cross-platform.
- **`Deserializer.cs` — cross-platform (no guard).** Inputs File Path + Run; no WinForms (path is a plain string, not a dialog). Deferral uses `Rhino.RhinoApp.Idle` (RhinoCommon, cross-platform). Compiles and runs on both TFMs as-is.

(Note: the old `Assemblies/AssemblyDefinition.cs` + `AssemblyIO.cs` that these conceptually replace were removed in commit f76f7d4; no `Disassembler.cs` was ever committed.)

### GhPythonBridge — Mac DLL paths to verify
Three DLLs need their Mac HintPaths confirmed against an actual Rhino 8 Mac install.
The csproj already has placeholder Mac ItemGroups with guessed paths:
- `Rhino.Runtime.Code.dll` — used directly; `ParamType` lives here
- `RhinoCodePlatform.GH.dll` — used directly; `IScriptComponent`, `ScriptParamSpec`, `ScriptParamAccess` live here
- `RhinoCodePlatform.GH1.dll` — in csproj but no longer imported in code (kept for completeness)

### GhPythonBridge — cross-platform compatibility

**Should work on Mac without changes (pure GH or reflection):**
- `IsScriptComponent` — `IScriptComponent` interface check, pure GH
- `SetScript` / `GetScript` — `IScriptComponent.Text`, in `RhinoCodePlatform.GH`
- `GetInputs` / `GetOutputs` — `IScriptComponent.Inputs/Outputs` + `IScriptParameter`, in `RhinoCodePlatform.GH`
- `GetErrors` / `GetWarnings` — `IGH_ActiveObject.RuntimeMessages`, pure GH
- `Expire` — `IGH_ActiveObject.ExpireSolution`, pure GH
- `GetInputValues` / `GetOutputValues` — `IGH_Component.Params.Input/Output` + `VolatileData`, pure GH
- `SetInputs` / `SetOutputs` — uses `ScriptParamSpec` + `ParamType.Any` (both in referenced DLLs) and reflection to call `UpdateInputParameters`/`UpdateOutputParameters` on `BaseScriptComponent`; reflection resolves at runtime so platform-agnostic once DLLs load

**Requires Mac DLL path verification before it can compile on Mac:**
- All of the above, because they depend on `RhinoCodePlatform.GH.dll` and `Rhino.Runtime.Code.dll` being resolvable at compile time
