---
name: component-rename-plainspoken
description: Four core GH components renamed to plain-spoken LLM terminology (2026-07-03) — old names are gone
metadata: 
  node_type: memory
  type: project
  originSessionId: e363c9ec-5b9e-4e8b-8711-07ffafd84439
---

Full-depth rename to plain-spoken, idiomatic harness terminology (2026-07-03). Display name, class, file, and all references changed; **ComponentGuids stay pinned** (GH serializes by GUID, so `.gh`/`.ghjson` round-trip is unaffected). Canvas NickName = the full new name for all four.

| Old | New display | New class / file | ComponentGuid (unchanged) |
|---|---|---|---|
| Chatbox | Chat | `Chat` / `Chat.cs` (+ `ChatboxAttrib`→`ChatAttrib`) | `B7E4B6F2-3C2A-4D71-9E0A-7F1C2D3E4A5B` |
| Composer | System Prompt | `SystemPrompt` / `SystemPrompt.cs` | `BA4FCD24-96DB-4B2B-B7F7-E756A98BC185` |
| Reasoner | LLM Call | `LlmCall` / `LlmCall.cs` | `F1097B2B-564A-43F8-8F70-BA6961F00E00` |
| Recorder | Conversation Log | `ConversationLog` / `ConversationLog.cs` | `43A02F6D-D97D-4241-B4DD-067D7AE0D75E` |

`Conversation` (the Core data type) was deliberately **kept** — already the standard term.

Also renamed: Core policy `ConversationRecorder` → `ConversationLogBuilder` (+ its test). Icons `Composer/Reasoner/Recorder.png` → `SystemPrompt/LlmCall/ConversationLog.png` (embedded by `Resources\*.png` wildcard; resolved by `GetType().Name`).

**Deliberately NOT renamed:** `GroundingComposer` (Core assembler — still composes); `Composer.svelte` (chat message-input widget — "composer" is correct UI parlance); the `"ChatboxEmoji"` persisted GH state key and `physalia.*` GhJSON extension keys (back-compat with saved files).

**Bridge verbs renamed in lockstep (C# ChatWindow ↔ Svelte bridge.ts/App.svelte):** `selectchatbox`→`selectchat`, `connectrecorder`→`connectconversationlog`, `setChatboxes`→`setChats`; TS type `UiChatbox`→`UiChat`; the `onconnectrecorder` child event → `onconnectconversationlog`.

**Ribbon section (2026-07-04):** the `subCategory` these four live in was renamed `Core` → `Pipeline` (functional name, like the sibling sections Grounding/Tools/Models/Signals; matches `PromptPipelineView`). Folder `Components/Core/` → `Components/Pipeline/` (C# namespaces are flat `Physalia.GH.Components`, so folder move is org-only — no namespace/using changes). `subCategory` is a static component property, NOT serialized in `.gh`/`.ghjson` — no round-trip impact; components just render under a "Pipeline" panel.

**Guardrails section (2026-07-04):** section `Deterministic Gates` → `Guardrails` (folder `Components/DeterministicGates/`→`Guardrails/`). Its components: Auditor → **Schema Validator** (class `SchemaValidator`; the one call to Core `Physalia.Core.Validation.SchemaValidator.Validate` is fully-qualified to avoid shadowing), Resolver → **Component Resolver** (`ComponentResolver`), Observer → **Canvas Observation** (`CanvasObservation`, the graph/error scanner), Output Snapshot → **Geometry Observation** (`GeometryObservation`, the viewport screenshot). **Detect JSON** (name unchanged) moved to the **Regulators** section.

**Grounding section (2026-07-04):** section name `Grounding` **kept** (already idiomatic). Library → **Component Catalog** (class `ComponentCatalogGrounder` — can't be `ComponentCatalog`, collides with the Core type), Image Gatherer → **Image Sources** (`ImageSources`); the four `*Grounder`s (Cluster/Python/Document Units/Canvas Inputs) unchanged.

Kept across these passes: Core `SchemaValidator`/`JsonExtractor`/`JsonDetector`/`ValidationError`; the `Prompt*Resolver` Core classes; GH-SDK `server.Libraries`/`LibraryGuid`; `ComponentCatalog`/`GH_ComponentCatalog`/`Param_ComponentCatalog` types; all `ComponentGuid`s.

Verified (all three passes): `dotnet build src/Physalia.slnx -c Debug` clean (0 errors), `dotnet test` 198/198 green. Live Rhino test still pending (ribbon panel names, canvas labels, pre-rename `.gh` round-trip, preset placement, chat switcher). See [[physalia-repo-gotchas]], [[chat-window]], [[grounding-on-recorder]], [[component-reorg-2026-07]].
