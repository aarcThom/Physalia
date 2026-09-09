# Physalia Project Memory

Grasshopper AI plugin for Rhino. Role, working dir, architecture, conventions: **CLAUDE.md** (authoritative; trust code over both). One line per memory below — detail lives in the topic files.

## General Preferences
- Search the internet (WebFetch, WebSearch) anytime it would help — don't hesitate.
- Record progress in MEMORY.md + topic files whenever meaningful progress is made; don't wait to be asked.
- Make code changes only when explicitly prompted ("make this change", "edit this", "fix this"). Otherwise advice only.
- [Commit/PR messages output-only](commit-and-pr-messages-output-only.md) — print in chat; `git commit` only on explicit instruction, never `push`/`gh`. Covers THAT batch, not the session.
- [Git workflow here](git-workflow-here.md) — **NEVER `git add -A`**: a CRLF worktree over an LF HEAD fakes ~770 modified files. Stage explicit paths, commit to `main`; `.claude/memory` is tracked.
- [Never `git checkout` a file to undo your own edit](git-checkout-discards-session-work.md) — it reverts to HEAD and destroys the session's work on that file.
- [Design fork, then build through](design-fork-then-build-through.md) — investigate the whole path, ask the ONE question the code cannot settle, then finish the vertical slice; say what has not run.

## Latest
- [Yak publishing](yak-publishing.md) — 2026-09-09, researched not pushed: the name is FREE, and `runtimes/` is **294MB of 321MB** — prune to win-x64. Plan: `planning/yak-publishing.md`.
- **[Data folder + update notice](data-folder-and-update-notice.md)** — 2026-09-09, dialog RUN LIVE: user files → `%LOCALAPPDATA%/Physalia`, shipped content OVERLAID. **Read before yak packaging.**
- [Project Prompts component](system-prompt-preambles.md) — 2026-09-08, built not run: System Prompt reads the HARNESS PROJECT folder, so a brief ships with the work. `.txt`/`.md` only.
- **[Preset build RUNBOOK](preset-build-runbook.md) — READ FIRST when asked to "build these presets"**: the `tools/presets/` toolkit, the build → verify → audit loop, and the five SILENT failures.
- [Teaching presets](teaching-presets.md) — 2026-09-09: the **28** annotated harnesses now SHIP as `Physalia/AI/01`–`28` behind the pink Experimental toggle; `wip_presets/` gone. All run live.
- [Preset conventions](preset-conventions.md) — **read before building a preset.** Naming, the TWO library roots, the tools-capable model rule, Picker discipline, and the STORED-vs-LOADED check.
- [Driving Rhino from WSL](driving-rhino-from-wsl.md) — the working script channel is SendKeys, not `RhinoCode`; why it silently stops; the two Python engines; the PATH such a process really gets.
- [Harness Builder meta-preset](harness-builder-preset.md) — 2026-09-07, RUN END TO END: a harness that WRITES harnesses. **FOUR component names are ambiguous** — resolve by name AND ribbon section.
- [ComfyUI render preset](comfy-render-preset.md) — 2026-09-07, RUN END TO END (viewport → SDXL → chat, ~19s). **comfy-mcp lists 39 tools on Windows then fails every call** — it must run in WSL.
- [PDF natives verified](pdf-natives-verified.md) — 2026-09-07, RUN IN RHINO: `PdfNativeLibrary` resolves and renders correctly. The node reads **`<project>/PDF`**, not the project folder itself.
- [MCP bridge + OAuth](mcp-integration.md) — 2026-09-07, RUN IN RHINO: a remote Streamable HTTP server beside a local stdio one, and OAuth reaches the browser bar the token exchange.
- [Blender MCP preset](blender-mcp-preset.md) — 2026-09-07, RUN LIVE: **the Claude Code provider IGNORES `tools`**, so it can never drive a Router — the pipeline stays green while being incapable.
- [Closing Grasshopper reaches nothing](gh-custom-attribute-traps.md) — 2026-09-07, panel RUN LIVE: **GH cancels its own close and HIDES**, and WinForms raises nothing when an ANCESTOR hides.
- [Pre-ship testing pass](pre-ship-testing-pass.md) — 2026-09-06/07: **A1–A6, B0–B3, B7, C1, C2 RUN LIVE and passing**; two defects found and re-verified. **F3 is the only ship blocker left.**
- [Trigger Control](trigger-tier.md) — 2026-09-06, host half RUN LIVE: arming moved into the chat window as a human tool. **Two arming verbs**, and triggers addressed by `InstanceGuid`, not nickname.
- [Building harnesses programmatically](building-harnesses-programmatically.md) — 2026-09-06/07: **`SetPersistentData` APPENDS**, a string becomes one item per CHARACTER, plus the document layer.
- [Watch Modelling](watch-modelling.md) — 2026-09-06, **RUN LIVE**: record what the USER does in Rhino as a repeatable procedure, off Rhino's command events. `CapturedCommandWindowStrings` is SHARED.
- [Trigger tier](trigger-tier.md) — 2026-09-06, one of five RUN LIVE: Physalia can START a round on its own (Timer / Folder Watcher / Rhino Changed / Data Changed). **Arming is never serialized.**
- [Signal conditionals](signal-relay-conditionals.md) — 2026-09-06, **ALL FIVE RUN LIVE**: Gate / Hold / Switch / Throttle + For Each. **A relay forwards the ORIGINAL signal**, never a re-mint.
- [Declare + Ask Human](intent-tools-declare-ask-human.md) — 2026-09-06: the model picks a route the pipeline offers, or asks a question. **Every question edge returns UNANSWERED, never a default.**
- [Harness delegation](harness-delegation.md) — 2026-09-06, **RUN LIVE**: a harness as a TOOL of another harness. Paired on the inner document with no wire; one task at a time; refuse up front.
- [Session budget + records](session-budget-and-records.md) — 2026-09-06, **VERIFIED ON DISK**: Budget Guard bounds a SESSION; `runs.jsonl` per call. **On a CLI provider a token cap is MEANINGLESS.**
- [Model API credentials](model-api-credentials.md) — 2026-09-04/06/09-09, BUILT not run in Rhino: providers configured in the chat window, DPAPI-encrypted; endpoint+key on one `GH_ModelApi` wire. **A local llama-server stores nothing, so it needed its own node** — LlamaCpp API.
- [Harness names + .phy packages](harness-names-and-phy-packages.md) — 2026-09-05, BUILT not run in Rhino: named `curious-cake-soap-fun` (DERIVED from the guid), own a project folder, save as `.phy`.
- [Project file tools](project-file-tools.md) — 2026-09-05, Core tested / GH not run: Project Folder grounding + `download_file` + `read_file`, fail-closed approval as a CARD. Sniff the bytes.
- [Chat link prompt + markdown restyle](chat-link-prompt.md) — 2026-09-05, headless-verified: **Tailwind never scans `node_modules`**, so streamdown's whole theme was dead in this bundle.
- [API Call tool](api-call-tool.md) — 2026-09-05, Core verified live / GH not run: the model reads a configured HTTP API, paging walked for it, ONE ITEM PER RECORD out. Catalog lives on the NODE.
- [MCP bridge vs Illustrator](mcp-bridge-chunked-body.md) — 2026-09-04: chunked POST bodies broke Adobe's server; a 404 on the optional GET stream killed the session. Fixed + `--trace`.
- [MCP setup page + YAML removal](mcp-setup-page.md) — 2026-09-04/05: paste a CLI command or fill the form. `MCP_SERVERS.YAML` GONE. An import deletes the file it read ONLY when something parsed.
- [run_rhino_script tool](run-rhino-script-tool.md) — 2026-09-03, WORKING live: `print` IS the read-back, so no `get_context` ships. Trap: RhinoCodePlugin loads ON DEMAND.
- [Building harnesses programmatically](building-harnesses-programmatically.md) — 2026-09-02: `EnsureInnerDocument()` + `ComponentServer.EmitObject`. Every backward path must be wireless.
- [Rhino MCP vs native control](rhino-mcp-vs-native-control.md) — 2026-09-02 decision record: McNeel's Rhino MCP can't travel in a preset (version-stamped router path).
- [MCP integration](mcp-integration.md) — 2026-08-27, BUILT: Physalia as MCP **client**. Measured: the official C# SDK cannot run in-process — hence stdio in-process + the `Physalia.McpBridge` relay.
- [Token Count human tool](token-count-human-tool.md) — 2026-08-24: counter moved onto its own grip-linked tool; the downstream-walk fallback is deleted.
- [CLI seeds carry their images](claudecode-warm-process.md) — 2026-08-22: both CLI providers stringified history on a RESEED, losing images. New `ConversationHelpers.ToSeedContent`. Verified live.
- [Image Mark Up tool](image-mark-up-tool.md) — 2026-08-21: snapshots/attachments open in an editor. Send mode had to INVERT (capture goes out, confirm comes back as a tagged submit).
- [Headless chat-UI testing](headless-chat-ui-testing.md) — 2026-08-21/09-05: drive `dist/index.html` in headless Chrome. Measures LAYOUT too — use the real 460x620 size and the inner SCROLLER.
- [Settings ownership](settings-ownership.md) — 2026-08-21: every setting serializes on the component it configures — a setting is only useful if it **ships inside a preset**.
- [Harness contents arrive unsolved](harness-subdocument.md) — 2026-08-21: nothing on the host canvas solves the sub-document. `HarnessComponent.PrimeInner()`.
- [Doc-comment validation is on](physalia-repo-gotchas.md) — 2026-08-21: `GenerateDocumentationFile` on all three projects; found 16 silent defects in one sweep.
- [Component description hooks](component-description-hooks.md) — 2026-08-21: shared bases expose **abstract** description properties, so a new component must describe its own signal to compile.
- [Harness I/O](harness-io.md) — 2026-08-19: Harness In / Harness Out. Inward is a real proxy param; Harness In is **passive** and binds by InstanceGuid, never position.
- [GH custom attribute traps](gh-custom-attribute-traps.md) — 2026-08-19: `NickName`'s setter raises nothing, and one hand-composed render channel skips ALL of `base.Render`.
- [Tool image attachments](tool-image-attachments.md) — 2026-08-18: a tool result is TEXT everywhere, so an image rides the answering turn as an ATTACHMENT, after every `ToolResultContent`.
- [Move In Space tool](move-in-space-tool.md) — 2026-08-17: adjacency DERIVED (8 cones × 3 bands, closest-per-bucket), world-frame directions. New `RegisterAdditionalOutputs` + `OnSolveEnd` hooks.
- [Harness sub-document](harness-subdocument.md) — 2026-08-17: residency guard GONE; "Load Harness from .gh File…". A discarded sub-document must be `RemoveObjects` + `Dispose`d, and ADOPT FIRST.
- [Feedback turn attribution](feedback-turn-attribution.md) — 2026-08-17: needed `PhySignal.Origins` **provenance**, because every aggregator re-mints under its own identity.
- [Merge Signal join](merge-signal-join.md) — 2026-08-17: every aggregator must use `SignalAggregation.Combine` — a signal's `ContentBlocks` are the WHOLE turn. It is a JOIN, not a passthrough.
- [Codex dynamic tools](codex-dynamic-tools.md) — 2026-08-16: Codex calls Physalia's tools with zero canvas changes; the call is DEFERRED back to the Router. Drop all text after a tool call.
- [Codex provider](codex-provider.md) — 2026-08-16/09-08: `codex app-server --stdio` JSON-RPC. **The model list is live**, so a new model needs no work; a too-new one 400s, a bad EFFORT is ignored.
- [C# Transmitter](csharp-transmitter.md) — 2026-08-11: C# declares params TWICE, so the push is gated on a signature check; every script transmitter must test `LanguageSpec`. Not run in Rhino.
- **2026-08-08: the HARNESS is the plug-in's base unit** — a real owned GH sub-document; presets are stock `.gh` files. `PhyDocuments` splits *local* from *host*. See [[harness-subdocument]].
- **2026-08-04: phy_critter is the project's ONLY logo** — `Resources/critter.png` + inlined in `HappyFace.svelte`; the jellyfish is DELETED. See [[chat-widget]].
- [Script I/O grounder](script-io-grounder.md) — 2026-07-31 (GUID pinned): emits the target's exact I/O as copyable JSON; the transmitter pushes code-only. Live test pending.
- [Compaction tool-pairing fix](compaction-tool-pairing-fix.md) — 2026-07-29: `Keep First = 2` halved a tool exchange and Anthropic 400'd. `Reassemble` pairs BOTH directions.
- [Human-tool taxonomy moves](human-tools-taxonomy-moves-2026-07.md) — 2026-07-29: Tools Present → Grounding; `/export` and the signal-trace widget reborn as Human Tools. No slash commands remain.
- [View Snapshot human tool](view-snapshot-human-tool.md) — 2026-07-28: geometry-free sibling of Geometry Snapshot; shared `SnapshotToolComponentBase`; `AcceptsPromptImages`.
- [Group-scoped grounding](group-scoped-grounding.md) — 2026-07-28: the master Physalia group auto-enrolls every LLM placement (per harness since 08-17); patch frame resolved by CHECKSUM. Proven live.
- [Incremental staged building](incremental-staged-building.md) — 2026-07-27: ONE measurable stage per response; plan block, Build Plan tracker, digest owns the report's closing line. Proven in Rhino.
- [Dead-wire lint + projected patch graph](dead-wire-lint-projected-graph.md) — 2026-07-26: unwired sliders and self-fed operators rejected; a ghpatch lints the graph it PRODUCES. Not run in Rhino.
- [Grouping + panel placement](grouping-and-panel-placement-fixes.md) — 2026-07-25: group-add schema deadlock; panels anchored by group membership; patch endpoint resolution.
- [Chat UI overhaul](chat-ui-overhaul-2026-07.md) — 2026-07-25: top-row human tools, action stack, recessed scrollbar, fade edges (oklab-seam gotcha).
- [Human tools split](human-tools-split.md) — 2026-07-23: LLM vs Human tool taxonomy; ConvLog 7-input reorder; image intake gated on Add Image.
- [Component-id robustness](component-id-robustness.md) — 2026-07-23: authored-id preservation hardened. Renumber root cause NOT pinned — watch for `Placement did not preserve`.
- [Geometry Snapshot grounding](geometry-snapshot-grounding.md) — 2026-07-23: the geometry button sends a viewport snapshot as its own message.
- [Balcony session debug](balcony-session-debug.md) — 2026-07-13: truncation root cause + 7 fixes; graft-169 mystery still open.
- [Thinking passthrough](thinking-passthrough.md) — 2026-07-11: inline `<think>` tags, stripped on resend, truncation warnings.
- [Signal Trace widget](signal-trace-widget.md) — 2026-07-10: 3 taps → static SignalTraceLog + Eto GridView.
- [Single-signal-output rework](single-signal-output-rework.md) — 2026-07-10: `HasFailOutput` opt-out + quiet `Fail(emitSignal:false)`.
- [Iterative placement robustness](iterative-placement-robustness.md) — 2026-07-08: Resolver made ghpatch-aware; JsonExtractor takes the LAST JSON block.

## Architecture & Lifecycle
- [v2 Core architecture](v2-core-architecture.md) — Core decisions locked 2026-05-03.
- [Signal lifecycle summary](signal-lifecycle-summary.md) — what the 2026-06 rework DELETED, the one-schedule-timer root cause, the locked decisions.
- [Signal carrier discipline](signal-carrier-discipline.md) — exactly Payload + ContentBlocks + Instructions; never add carrier fields.
- [Conversation compaction](conversation-compaction.md) — window/prune/summarize; Instructions ride the signal.
- [Component reorg 2026-07](component-reorg-2026-07.md) — ribbon sections + folders; GH_Exposure forces intra-tab order.
- [Plain-spoken rename](component-rename-plainspoken.md) — Chatbox→Chat, Composer→System Prompt, Reasoner→LLM Call, Recorder→Conversation Log (GUIDs pinned).

## Refactoring
- [DRY refactor 2026-06](dry-refactor-2026-06.md) — shared bases: ProtocolProviderBase, HttpErrorMapper, PhyGoo/PhyParam, GripLinkAttrib.
- [Tier-1 refactoring](tier1-refactoring.md) — `Physalia.Core.Tests` (xUnit), provider fixtures, pure-policy extractions.
- [Arrow DRY refactor](arrow-dry-refactor.md) — one `ArrowGrip` + `IArrowHost`; central `ArrowStyles`.

## GhJSON (canvas import/export)
- [GhJSON library is reference-only](ghjson-library-reference-only.md) — third-party downloads; NEVER modify, consume via nuget.
- [GhJsonBridge façade](ghjsonbridge-facade.md) — location + nickName round-trip, Put-mutates-live-doc deferral.
- [Iterative canvas editing](iterative-canvas-editing.md) — canvas-state grounding + ghpatch dual-mode CompTx.
- [Component Transmitter](component-transmitter.md) — places an LLM GhJSON graph, routes placement errors back on Fail.
- [System-prompt preambles](system-prompt-preambles.md) — PREAMBLE + SCHEMA from `Files/SYSTEM_PROMPTS` (.txt/.json/.yaml, NOT .md); `Additional Prompt` registered LAST.
- [Obsolete component GUID validation](obsolete-component-guid-validation.md) — `StampComponentGuids` stamps non-obsolete GUIDs at placement.
- [Slider nicknames](slider-nicknames.md) — LLM-placed sliders get real labels. Its PhySchema half is stale.
- [GhJSON feedback links](ghjson-feedback-links.md) — wireless links round-trip via component-id extensions + IdToGuidMapping remap.
- [Picker selection persistence](picker-ghjson-serialization.md) — `physalia.pickerValue`; the **provisional-list trap** fixed by `PickableInput.IsSettled`.
- Pinned: GhJSON.Grasshopper needs Grasshopper/RhinoCommon 8.24.25281.15001 (both `ExcludeAssets="runtime"`). `WriteOptions` in `GhJSON.Core.Serialization`.

## Chat UI & Canvas
- [Chat window](chat-window.md) — Eto WebView + Svelte/shadcn UI; bundle EMBEDDED in the assembly, extracted to temp.
- [Chat widget](chat-widget.md) — bottom-right widget; finds a Chat anywhere or creates a detached one. Places NOTHING.
- [Harness sub-document](harness-subdocument.md) — the base unit: an owned `GH_Document` behind a proxy node.
- [Chat window placement fixes](chat-window-placement-fixes.md) — full-name `ExpireLayout`; centred over the GH editor.
- [UI design: neumorphism](ui-design-neumorphism.md) — `--neu-*` tokens; the two edge-shadow gotchas.
- [Chatbox switcher row](chatbox-switcher-row.md) — bottom circles switch the one window between Chats.
- [Chatbox emoji identity](chatbox-emoji-identity.md) — random ocean emoji as canvas icon + switcher dot.
- [Chat token counter](chat-token-counter.md) — **superseded** by [[token-count-human-tool]].
- [GH no-preview Hidden palette](gh-nopreview-hidden-palette.md) — GH forces non-preview nodes onto `GH_Palette.Hidden`.
- [Resources tab + Image Gatherer](resources-tab-image-gatherer.md) — ImageResource goo; the Eto/WPF GridView edit-commit gotcha.
- [Prompter image references](prompter-image-references.md) — `/<alias>` inline images; where the feature went when Prompter was deleted.

## Grounding, Tools & Models
- [Grounding on Conversation Log](grounding-on-recorder.md) — grounding moved off System Prompt; opt-in nullable selection.
- [Document Units grounding](document-units-grounding.md) — units override is text-to-LLM only, never changes the doc.
- [Rhino Geometry tool + /t/ refs](rhino-geometry-tool-and-slash-t.md) — bakes geo + drops a referencing param; `/t/<tool>` prompt refs.
- [Memory tool](memory-tool.md) — provider-agnostic `memory` tool; global + local scopes under `Files/MEMORIES`.
- [RhinoCommon RAG tool](rhinocommon-rag-tool.md) — `search_rhinocommon` over a reflection+XML-doc merge index.
- [Web search tools](web-search-tools.md) — `web_search` (Tavily) + `read_url` (Jina, keyless); `Core/Web/WebTools.cs`.
- [Tools In Use component](tools-in-use-component.md) — scans for tool nodes wired to a Router, emits their definitions.
- [Detect JSON gate](detect-json-gate.md) — attempted JSON → Success; plain chat → Fail quietly.
- [Model Information](model-information-and-minified-prompts.md) — merges OpenRouter+LiteLLM with id normalization.
- [GH_ApiKey goo](gh-apikey-goo.md) — **SUPERSEDED** by [[model-api-credentials]]; replaced by `GH_ModelApi`.
- [Tool calling Phase 4](tool-calling-phase4.md) — `StreamAsync` gained a tools list.
- [GH tool-calling loop](tool-calling-gh-loop.md) — Router aggregates results per round; one result per call.
- [ClaudeCode warm process](claudecode-warm-process.md) — ONE warm `claude` CLI per LLM Call; the SDK is a dead end.
- [ClaudeCode provider perf](claudecode-provider-perf.md) — visible thinking needs the UNDOCUMENTED `--thinking enabled --thinking-display summarized`; `MAX_THINKING_TOKENS` is the wrong lever.

## Platform / Build
- [Driving Rhino from WSL](driving-rhino-from-wsl.md) — build with the WINDOWS dotnet; `Rhino.exe /runscript` is unreliable from WSL; `RhinoCode.exe script` reports success and runs nothing.
- [Core console harness](core-console-harness.md) — test a provider from a throwaway net7.0 console app; no Rhino.
- [Inspecting Rhino assemblies](inspecting-rhino-assemblies.md) — reflect over Rhino 8's shipped DLLs from PowerShell.
- [SVG → transparent PNG](svg-rasterization-headless-chrome.md) — no magick/inkscape here; use headless Chrome.
- [Component icon generation](component-icon-generation.md) — splitter in `tools/icons/`; **+29 added 2026-09-07 → 0 fallbacks**, though 109 components now share 108 icons (`LlamaCppApi` borrows one via `IconPath`). A bead-size drift measured FALSE — check ink before Split.ps1.
- [New machine bring-up](new-machine-bringup.md) — 2026-09-07: a clean Debug build leaves `Bridge/` EMPTY, so every REMOTE MCP server reports the bridge missing from a green build. Five more traps.
- [Physalia repo gotchas](physalia-repo-gotchas.md) — slnx in `src/`; the `Files` → bin pipeline + its two MSBuild gotchas.
- [ILRepack Release double-merge](ilrepack-release-double-merge.md) — the empty `ILRepack.targets` suppresses the package's failing target. Don't delete it.
- [Mac todo](mac-todo.md) — **superseded by `planning/mac-port.md`** (2026-09-07, release deferred). Two blockers found FROM WINDOWS; 29 WinForms files but only 4 real items; Core is completely clean.
- [GH code editor abandoned](gh-code-editor-abandoned.md) — native GH script editor unreachable; custom Eto dialog instead.
- [Python output list access](python-output-list-access.md) — RESOLVED 2026-06-29. Fix = `MarshOutputs` on, plus No Type Hint + List access.
- Two projects only: Physalia.Core (net7.0), Physalia.GH (net7.0-windows / net7.0 on Mac). CA1416 warnings are false positives.

## Meta
- [Memory sync setup](memory-sync-setup.md) — CLAUDE.md + this memory sync via git; canonical files in repo `.claude/memory/`, global path is a junction.
