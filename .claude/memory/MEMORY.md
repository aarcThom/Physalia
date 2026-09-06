# Physalia Project Memory

Grasshopper AI plugin for Rhino. Role, working dir, architecture, conventions: **CLAUDE.md** (authoritative; trust code over both). One line per memory below — detail lives in the topic files.

## General Preferences
- Search the internet (WebFetch, WebSearch) anytime it would help — don't hesitate.
- Record progress in MEMORY.md + topic files whenever meaningful progress is made; don't wait to be asked.
- Make code changes only when explicitly prompted ("make this change", "edit this", "fix this"). Otherwise advice only.
- [Commit/PR messages output-only](commit-and-pr-messages-output-only.md) — print in chat; `git commit` only on explicit instruction, never `push`/`gh`. Covers THAT batch, not the session.
- [Never `git checkout` a file to undo your own edit](git-checkout-discards-session-work.md) — it reverts to HEAD and destroys the session's work on that file.
- [Design fork, then build through](design-fork-then-build-through.md) — investigate the whole path, ask the ONE question the code can't settle, then finish the vertical slice (Core→GH→UI, tests, docs) and say what hasn't run live.

## Latest
- [Chat link prompt](chat-link-prompt.md) — 2026-09-05, fixed (headless-verified, not in Rhino): the external-link warning rendered unstyled over the conversation. **Tailwind never scans `node_modules`**, so streamdown's whole theme is dead here, and its `window.open` confirm reaches no browser — the chat now shows its OWN prompt and opens links through the host.
- [API Call tool](api-call-tool.md) — 2026-09-05, Core verified live / GH not run in Rhino: model reads a configured HTTP API. Own plain store, key in shared `credentials.dat`, catalog on the NODE. The tool walks paging itself and delivers ONE ITEM PER RECORD. Any tool is now pipeline-drivable via a `manual:` call id. Two general rules earned live: store-backed nodes reload on a file STAMP not on emptiness, and **an unset setting must name itself and who can change it** — a safe default is not a visible one.
- [Model API credentials](model-api-credentials.md) — 2026-09-04/05, BUILT not run in Rhino: providers configured in the chat window, DPAPI-encrypted; endpoint+key on one `GH_ModelApi` wire (**new GUID**). Both YAML config files deleted.
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
- [Component description hooks](component-description-hooks.md) — 2026-08-21: shared bases expose **abstract** description properties, so a new component can't compile without describing its own signal.
- [Harness I/O](harness-io.md) — 2026-08-19: Harness In / Harness Out. Inward is a real proxy param; Harness In is **passive** and binds by InstanceGuid, never position.
- [GH custom attribute traps](gh-custom-attribute-traps.md) — 2026-08-19: `NickName`'s setter raises nothing, `ExpireLayout` is not a promise `Layout()` runs, a MOVE raises nothing, hand-composing one render channel skips all of `base.Render`.
- [Tool image attachments](tool-image-attachments.md) — 2026-08-18: `take_snapshot`. A tool result is TEXT everywhere, so an image rides the answering turn as an ATTACHMENT, ordered AFTER every `ToolResultContent`.
- [Move In Space tool](move-in-space-tool.md) — 2026-08-17: adjacency DERIVED (8 cones × 3 bands, closest-per-bucket), world-frame directions. New `RegisterAdditionalOutputs` + `OnSolveEnd` hooks.
- [Harness sub-document](harness-subdocument.md) — 2026-08-17: residency guard GONE; "Load Harness from .gh File…". A discarded sub-document must be `RemoveObjects` + `Dispose`d, and ADOPT FIRST.
- [Feedback turn attribution](feedback-turn-attribution.md) — 2026-08-17: needed `PhySignal.Origins` **provenance**, because every aggregator re-mints under its own identity.
- [Merge Signal join](merge-signal-join.md) — 2026-08-17: every aggregator must use `SignalAggregation.Combine` — a signal's `ContentBlocks` are the WHOLE turn. It is a JOIN, not a passthrough.
- [Codex dynamic tools](codex-dynamic-tools.md) — 2026-08-16: Codex calls Physalia's tools with zero canvas changes; the call is DEFERRED back to the Router. Drop all text after a tool call.
- [Codex provider](codex-provider.md) — 2026-08-16: `codex app-server --stdio` JSON-RPC (NOT `codex exec`). Reasoning needs `summary:"auto"`; a server-initiated request must be ANSWERED. Not run in Rhino.
- [C# Transmitter](csharp-transmitter.md) — 2026-08-11: C# declares params TWICE, so the push is gated on a signature check; every script transmitter must test `LanguageSpec`. Not run in Rhino.
- **2026-08-08: the HARNESS is the plug-in's base unit** — a real owned GH sub-document; presets are stock `.gh` files. `PhyDocuments` splits *local* from *host*. See [[harness-subdocument]].
- **2026-08-04: phy_critter is the project's ONLY logo** — `Resources/critter.png` + inlined in `HappyFace.svelte`; the jellyfish is DELETED. See [[chat-widget]].
- [Script I/O grounder](script-io-grounder.md) — 2026-07-31 (renamed from "Interface Lock", GUID pinned): emits the target's exact I/O as copyable JSON; transmitter pushes code-only. Live test pending.
- [Compaction tool-pairing fix](compaction-tool-pairing-fix.md) — 2026-07-29: `Keep First = 2` cut a tool exchange in half and Anthropic 400'd. `Reassemble` pairs BOTH directions. Gemini tool calls still unparsed.
- [Human-tool taxonomy moves](human-tools-taxonomy-moves-2026-07.md) — 2026-07-29: Tools Present → Grounding; `/export` and the signal-trace widget reborn as Human Tools. No slash commands remain.
- [View Snapshot human tool](view-snapshot-human-tool.md) — 2026-07-28: geometry-free sibling of Geometry Snapshot; shared `SnapshotToolComponentBase`; `AcceptsPromptImages`.
- [Group-scoped grounding](group-scoped-grounding.md) — 2026-07-28: the master Physalia group auto-enrolls every LLM placement (per harness since 2026-08-17); patch frame resolved by checksum MATCHING. Proven live.
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
- [Core console harness](core-console-harness.md) — test a provider from a throwaway net7.0 console app; no Rhino.
- [Inspecting Rhino assemblies](inspecting-rhino-assemblies.md) — reflect over Rhino 8's shipped DLLs from PowerShell.
- [SVG → transparent PNG](svg-rasterization-headless-chrome.md) — no magick/inkscape here; use headless Chrome.
- [Component icon generation](component-icon-generation.md) — splitter in `tools/icons/`; whole set replaced 2026-08-17.
- [Physalia repo gotchas](physalia-repo-gotchas.md) — slnx in `src/`; the `Files` → bin pipeline + its two MSBuild gotchas.
- [ILRepack Release double-merge](ilrepack-release-double-merge.md) — the empty `ILRepack.targets` suppresses the package's failing target. Don't delete it.
- [Mac todo](mac-todo.md) — four `#if WINDOWS` files, 22 more importing WinForms unguarded, GhPythonBridge HintPaths.
- [GH code editor abandoned](gh-code-editor-abandoned.md) — native GH script editor unreachable; custom Eto dialog instead.
- [Python output list access](python-output-list-access.md) — RESOLVED 2026-06-29. Fix = `MarshOutputs` on, plus No Type Hint + List access.
- Two projects only: Physalia.Core (net7.0), Physalia.GH (net7.0-windows / net7.0 on Mac). CA1416 warnings are false positives.

## Meta
- [Memory sync setup](memory-sync-setup.md) — CLAUDE.md + this memory sync via git; canonical files in repo `.claude/memory/`, global path is a junction.
