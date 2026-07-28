# Physalia Project Memory

Grasshopper AI plugin for Rhino. Role, working dir, architecture, conventions: **CLAUDE.md** (authoritative; trust code over both). One line per memory below — detail lives in the topic files.

## General Preferences
- Search the internet (WebFetch, WebSearch) anytime it would help — don't hesitate.
- Record progress in MEMORY.md + topic files whenever meaningful progress is made; don't wait to be asked.
- Make code changes only when explicitly prompted ("make this change", "edit this", "fix this"). Otherwise advice only.
- [Commit/PR messages output-only](commit-and-pr-messages-output-only.md) — print them in chat; never run `git commit`/`push`/`gh`.

## Latest
- [Incremental staged building](incremental-staged-building.md) — 2026-07-27: the model places ONE measurable stage per response; plan block in prose, Build Plan tracker, digest owns the Geometry Report's closing line. NOT yet run in Rhino.
- [Dead-wire lint + projected patch graph](dead-wire-lint-projected-graph.md) — 2026-07-26: unwired sliders and self-fed operators now rejected; a ghpatch lints the graph it PRODUCES. Report-side gaps (driving scalars, checksum-diff attribution) still open. NOT yet run in Rhino.
- [Grouping + panel placement](grouping-and-panel-placement-fixes.md) — 2026-07-25: group-add schema deadlock (Physalia forbade `id`, library required it) + `not`-branch error noise; panels anchored by group membership; patch endpoint resolution + duplicate-wire dedup.
- [Chat UI overhaul](chat-ui-overhaul-2026-07.md) — 2026-07-25: top-row human tools, action stack on prompt box, recessed scrollbar, fade edges (oklab-seam gotcha).
- [Human tools split](human-tools-split.md) — 2026-07-23: LLM Tools vs Human Tools taxonomy; HumanTool union; ConvLog 7-input reorder; image intake gated on Add Image.
- [Component-id robustness](component-id-robustness.md) — 2026-07-23: authored-id preservation hardened; canvas ids in reports. White House renumber root cause NOT pinned — watch for the `Placement did not preserve` log.
- [Geometry Snapshot grounding](geometry-snapshot-grounding.md) — 2026-07-23: composer geometry button sends a viewport snapshot as its own message (never auto-attached).
- [Balcony session debug](balcony-session-debug.md) — 2026-07-13: truncation root cause + 7 fixes applied; graft-169 mystery still open.
- [Thinking passthrough](thinking-passthrough.md) — 2026-07-11: inline `<think>` tags, stripped on resend, truncation warnings, Anthropic thinking-budget controls.
- [Signal Trace widget](signal-trace-widget.md) — 2026-07-10: signal debugging via 3 taps → static SignalTraceLog + Eto GridView + canvas widget.
- [Single-signal-output rework](single-signal-output-rework.md) — 2026-07-10: `HasFailOutput` opt-out + quiet `Fail(emitSignal:false)`; Stall Guard inputs reordered.
- [Iterative placement robustness](iterative-placement-robustness.md) — 2026-07-08: Resolver made ghpatch-aware, JsonExtractor takes LAST JSON block, feedback carries nickname+instanceGuid.

## Architecture & Lifecycle
- [v2 Core architecture](v2-core-architecture.md) — full Core decisions locked 2026-05-03. Specs: `planning/physalia-primitives.md`, `planning/api_research.md`.
- [v2 planned architecture](v2-architecture.md) — the older v0.2 pipeline-decomposition plan the above superseded; historical context only.
- [Signal lifecycle summary](signal-lifecycle-summary.md) — what the rework DELETED + the multimodal ContentBlocks extension. Authoritative: `planning/data-marshalling.md`.
- [Signal carrier discipline](signal-carrier-discipline.md) — PhySignal carries exactly Payload + ContentBlocks + Instructions; never add carrier fields (god-object guard).
- [Trigger state machine status](trigger-state-machine-status.md) — marshalling history + the locked decisions not to relitigate.
- [Routing trigger system](routing-trigger-system.md) — pre-PhySignal routing/trigger design, kept only as a non-repo leftover; superseded, don't build on it.
- [Conversation compaction](conversation-compaction.md) — window/prune/summarize in `Core/Compaction/`; Instructions ride the signal, inline `Conversation Log → Compactor → LLM Call`.
- [Component reorg 2026-07](component-reorg-2026-07.md) — ribbon sections + folders; GH_Exposure forces intra-tab order (tab order itself is alphabetical).
- [Plain-spoken rename](component-rename-plainspoken.md) — Chatbox→Chat, Composer→System Prompt, Reasoner→LLM Call, Recorder→Conversation Log (GUIDs pinned).

## Refactoring
- [DRY refactor 2026-06](dry-refactor-2026-06.md) — shared bases: ProtocolProviderBase, HttpErrorMapper, PhyGoo/PhyParam, GripLinkAttrib, Model/Tweaker bases.
- [Tier-1 refactoring](tier1-refactoring.md) — `Physalia.Core.Tests` (xUnit), provider fixtures, token-estimator interface split, pure-policy extractions. Tier-2 deferred; plan in `planning/refactoring.md`.
- [Arrow DRY refactor](arrow-dry-refactor.md) — drag-arrow logic unified into one `ArrowGrip` + `IArrowHost`; pluggable arrowheads; central `ArrowStyles`.

## GhJSON (canvas import/export)
- [GhJSON library is reference-only](ghjson-library-reference-only.md) — local ghjson-dotnet/ghjson-spec are third-party downloads; NEVER modify, consume via nuget (1.1.1+).
- [GhJsonBridge façade](ghjsonbridge-facade.md) — location + nickName round-trip, Put-mutates-live-doc deferral, canvas HUD transform trick.
- [Iterative canvas editing](iterative-canvas-editing.md) — canvas-state grounding + ghpatch dual-mode CompTx; patch mode edits in place. CanvasInputGrounding retired.
- [Component Transmitter](component-transmitter.md) — places an LLM GhJSON graph, routes placement errors + orphan wiring back on Fail.
- [System-prompt preambles](system-prompt-preambles.md) — PREAMBLE + SCHEMA from `Files/SYSTEM_PROMPTS` (.txt/.json/.yaml only, NOT .md); exactly two pairs.
- [Obsolete component GUID validation](obsolete-component-guid-validation.md) — `StampComponentGuids` stamps non-obsolete GUIDs at placement (library's `Put` falls back to unfiltered `CreateByName`).
- [Slider nicknames](slider-nicknames.md) — LLM-placed sliders get real labels; `ApplyNickNameDisplay` no longer clobbers floating-param nicknames.
- [GhJSON feedback links + comment](ghjson-feedback-links.md) — Feedback→Collector wireless links round-trip via component-id extensions + IdToGuidMapping remap.
- [Picker GhJSON serialization](picker-ghjson-serialization.md) — Picker selection round-trips via `physalia.pickerValue`; labels only, never secrets.
- Pinned versions: GhJSON.Grasshopper requires Grasshopper/RhinoCommon 8.24.25281.15001 (both `ExcludeAssets="runtime"`; benign MSB3277 GH_IO warning on 8.31 dev machines). `WriteOptions` lives in `GhJSON.Core.Serialization`, `Indented` defaults true.

## Chat UI & Canvas
- [Chat window](chat-window.md) — Eto WebView + full Svelte/shadcn UI in `src/Physalia.UI`; bundle EMBEDDED in the assembly, extracted to temp at runtime. Plan: `planning/chat-window.md`.
- [Chat widget](chat-widget.md) — bottom-right canvas widget opens the chat window; find-or-creates a Chat; `IsPipelineReady` setup detection.
- [UI design: neumorphism](ui-design-neumorphism.md) — `--neu-*` tokens + `.neu-*` helpers; the two edge-shadow gotchas (gutter clip, overflow-hidden clips child shadows).
- [Chatbox switcher row](chatbox-switcher-row.md) — bottom circles switch the one window between Chat components; `selectchatbox` bridge verb.
- [Chatbox emoji identity](chatbox-emoji-identity.md) — random ocean emoji as canvas icon + switcher dot (TextRenderer colour emoji, deduped, persisted).
- [Chat token counter](chat-token-counter.md) — mirrors the TokenEstimator downstream of the viewed chat's Conversation Log; hidden when none wired.
- [Preset placement](preset-placement.md) — "Add preset" splices the live Chat into the preset's placeholder slot; + `ExpandToFullName` ExpireLayout fix; window centres over the GH editor.
- [Collapsible harness](collapsible-harness.md) — hide/show a Chat's pipeline behind the proxy node (in-place visual collapse, not a cluster); `src/Physalia.GH/Harness/`.
- [Collapsed Chatbox arrow](collapsed-chatbox-arrow.md) — proxy shows a delegated bottom drag arrow (`IHarnessArrow`) when it holds exactly one transmitter.
- [GH collapsed-harness grips](gh-collapsed-harness-grips.md) — block wire-drag by gating PARAM-attribute HasInput/OutputGrip, not the component; hidden members leak grips at the proxy pivot.
- [GH no-preview Hidden palette](gh-nopreview-hidden-palette.md) — to tint a signal-only component via GH_Skin, swap the **Hidden** palette: GH forces non-preview nodes onto `GH_Palette.Hidden`.
- [Resources tab + Image Gatherer](resources-tab-image-gatherer.md) — ImageResource goo, path-only persistence, and the Eto/WPF GridView edit-commit gotcha (two crashes).
- [Prompter image references](prompter-image-references.md) — `/<alias>` inline images: Core parser, signal flow, alias rules, PrompterAttrib grip gotcha.

## Grounding, Tools & Models
- [Grounding on Conversation Log](grounding-on-recorder.md) — grounding moved off System Prompt + chat-UI two-level tab/panel selector; opt-in nullable selection serialized.
- [Document Units grounding](document-units-grounding.md) — 4th grounding kind + chat pill with a units override (text-to-LLM only, never changes the doc).
- [Rhino Geometry tool + /t/ refs](rhino-geometry-tool-and-slash-t.md) — `create_rhino_geometry` bakes geo + drops a referencing param; Tools on `Instructions.Tools`; `/t/<tool>` prompt refs; CompTx reuses placed params.
- [Memory tool](memory-tool.md) — provider-agnostic Anthropic-style `memory` tool; global + per-`.gh` scopes under `Files/memories`.
- [RhinoCommon RAG tool](rhinocommon-rag-tool.md) — `search_rhinocommon` tool node over a reflection+XML-doc merge index, for code-gen grounding.
- [Web search tools](web-search-tools.md) — `web_search` (Tavily) + `read_url` (Jina, keyless); `Core/Web/WebTools.cs`; keys via the `web_search` YAML section.
- [Tools In Use component](tools-in-use-component.md) — scans the doc for tool nodes wired to a Router, emits their definitions as one list.
- [Detect JSON gate](detect-json-gate.md) — presence gate: attempted JSON (even malformed) → Success; plain chat → Fail quietly. `JsonDetector` in Core/Validation.
- [Model Information + minified prompts](model-information-and-minified-prompts.md) — merges OpenRouter+LiteLLM with id normalization. Research: `planning/{deterministic-gates,tool-components,model-information}.md`.
- [GH_ApiKey goo](gh-apikey-goo.md) — API keys flow as a typed label-only goo, never plain text, never serialized.
- [Tool calling Phase 4](tool-calling-phase4.md) — provider contract SENDS tool definitions (`StreamAsync` gained a tools list).
- [GH tool-calling loop](tool-calling-gh-loop.md) — Router aggregates results per round; tool nodes inherit the multi-call contract (one result per call).
- [ClaudeCode warm process](claudecode-warm-process.md) — ONE warm `claude` CLI per LLM Call (stream-json); the SDK is a dead end (no .NET, needs an API key).
- [ClaudeCode provider perf](claudecode-provider-perf.md) — the freeze was extended thinking (`MAX_THINKING_TOKENS=0`); `--safe-mode` keeps OAuth, `--bare` breaks it; pipes pinned no-BOM UTF-8.

## Platform / Build
- [Physalia repo gotchas](physalia-repo-gotchas.md) — slnx in `src/`; the `Files` → bin pipeline + its two MSBuild gotchas (stray `src/Physalia.GH/Files`, VS one-build UI lag needing `DisableFastUpToDateCheck` on BOTH projects).
- [Mac todo](mac-todo.md) — Windows-only / unverified-on-Mac surfaces: PrompterAttrib WinForms, the Serializer `#if WINDOWS` split, GhPythonBridge DLL HintPaths.
- [GH code editor abandoned](gh-code-editor-abandoned.md) — native GH script editor unreachable (Eto 2.7-vs-2.11 CS1705 + a lifecycle requirement); custom Eto dialog instead.
- [Python output list access](python-output-list-access.md) — **STILL BROKEN.** RhinoCode forces `AutoDeclare=!HasInstance`→Item on first push. Next: force true "No Type Hint" converter + read-back.
- Two projects only: Physalia.Core (net7.0), Physalia.GH (net7.0-windows on Windows / net7.0 on Mac). CA1416 System.Drawing warnings are false positives — Rhino ships a compatibility layer; suppress.

## Meta
- [Memory sync setup](memory-sync-setup.md) — CLAUDE.md + this memory sync across machines via git; canonical files in repo `.claude/memory/`, global path is a junction. New machine needs a one-time junction.
