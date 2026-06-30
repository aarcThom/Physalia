---
name: grounding-on-recorder
description: Grounding moved from Composer to Recorder + a chat-UI two-level selector; opt-in nullable selection serialized on the Recorder
metadata: 
  node_type: memory
  type: project
  originSessionId: b1834613-b361-4098-9b0b-533b57b7ce65
---

Grounding input moved **off Composer onto the Recorder** (2026-06-29), with a chat-window selector to narrow which installed components are folded into the system prompt. Plan: `C:\Users\rober\.claude\plans\i-want-to-remove-dreamy-hearth.md`. Builds clean (C# 0 err, svelte-check 0 err, 119 Core tests green). **Live Rhino test still pending.**

**Model (opt-in, nullable):** selection is `null` = include everything (default — preserves "grounding is appended by default"); non-null = include exactly the listed `(Category, SubCategory)` leaves. Keyed `Category→set<SubCategory>` because panel names aren't globally unique. See [[signal-carrier-discipline]] (grounding does NOT ride the signal — it's a Recorder input + setting).

**Core (pure):** the whole catalog namespace was moved `Physalia.Core.Catalog` → **`Physalia.Core.Grounding.Components`** (folder `Physalia.Core/Grounding/Components/`: ComponentCatalog, CatalogEntry, CatalogCategory, ComponentMatcher) so it nests under Grounding (resolves the old Catalog↔Grounding circular using; `ComponentCatalog` sees `GroundingSelection` via the enclosing namespace, no using). New: `Components/CatalogCategory.cs` (tab→panels record); `Grounding/GroundingSelection.cs` (All/FromLeaves/Includes/With/Leaves); `ComponentCatalog` gained `CategoryTree` (lazy) + `Filtered(selection)` (null ⇒ same instance). Tests under `Physalia.Core.Tests/Grounding/Components/ComponentCatalogTests.cs` + `Grounding/GroundingSelectionTests.cs`.

**Recorder** (`Components/Core/Recorder.cs`): new `Grounding` input at index 1 — second input, right after System Prompt, before the four signal inputs (list, optional); reads it every solve, caches `_liveCatalog`/`_liveGroundings`; at mint, filters only `ComponentCatalogGrounding` via `_selection` then `GroundingComposer.Append`. Public: `AvailableGroundingTree`, `HasComponentGrounding`, `GroundingSelectionOrNull`, `SetGroundingSelection`. **Now overrides Write/Read** (it serialized nothing before) — persists selection with a `GroundingSelectionSet` bool to keep null-vs-empty distinct; `OnCleared` deliberately leaves `_selection` (config, not conversation).

**Composer** (`Components/Core/Composer.cs`): grounding input + `GroundingComposer.Append` block removed; outputs unchanged. Bundled presets needed **no** re-wiring (they route the Library catalog to tool components, not grounding; no connection ever targeted Composer's old input 2).

**Serialization for presets** (`Generation/GhJsonBridge.cs`): `physalia.groundingSelection` extension mirroring the Picker's `physalia.pickerValue` exactly — `InjectGroundingSelection` (export, skips null) + `RestoreGroundingSelection` (returns bool to trigger re-solve, like [[picker-ghjson-serialization]]).

**ChatWindow** (`Panels/ChatWindow.cs`): Tick state gained `groundingWired`/`groundingTree`/`groundingSelection` (+ `_lastGroundingSignature` cache, reset in `ResetPushedState`); new `setgrounding` bridge verb (`?sel={all,leaves}` JSON) → `SetGroundingSelection`.

**UI:** `bridge.ts` UiState + `GroundingCategory`/`GroundingSelectionPayload`; Composer.svelte got a Layers grounding icon stacked above the image button (greyed when `!groundingWired`); new `lib/chat/Grounding.svelte` (kinds-pill view → two-level tristate tab→panel tree, "Reset to all" returns to null). Wired in `App.svelte`. Pills page is data-driven so cluster/python kinds slot in later.
