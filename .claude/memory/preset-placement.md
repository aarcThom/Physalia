---
name: preset-placement
description: "Preset placement splices the live Chat into the preset's placeholder Chat slot, anchored layout; plus full-name ExpireLayout fix and chat window centring over the GH editor"
metadata: 
  node_type: memory
  type: project
  originSessionId: 5eb77ca2-b5d4-41b5-99ed-df20ad19d9aa
  modified: 2026-08-09T05:29:46.329Z
---

**SUPERSEDED 2026-08-08 (section 1 only).** A preset is now a **stock `.gh` file in `Files/Presets`**,
not a `.ghjson`: it holds one harness's worth of pipeline, so `HandlePlacePreset` reads the archive
(`HarnessComponent.ReadDocumentFile`, chunk `"Definition"`, direct `GH_Archive` rather than
`GH_DocumentIO.Open` — which stamps FilePath and pollutes GH's recent-files MRU) and hands it to
`HarnessComponent.ReplaceInnerDocument`, then re-points the window at the Chat inside. **No splice,
no placeholder, no re-wiring** — `LoadAndPlaceAnchored`, `RewireAnchor` and `RewireRequest` are
DELETED, along with `TryReadMetadataDescription` (a `.gh` carries no readable description, so the
gallery shows file names only). Authoring a preset = save from inside a harness. See
[[collapsible-harness]]. Sections 2 and 3 below still stand.

Session 2026-06-23 changes to how the chat window's "Add preset" places a bundled `.ghjson` workflow. Extends [[chat-window]]. Builds clean (`dotnet build src/Physalia.slnx`); live-Rhino verification still pending.

## 1. Preset placement splices in the live Chat (no duplicate) — SUPERSEDED, see above
A preset's first `Chat` component is a **placeholder slot** — it must NOT be instantiated. The window's already-placed live `Chat` (`_component`) is spliced in for it, and the whole workflow is laid out relative to where that live Chat sits. Before this fix, `HandlePlacePreset` called `GhJsonBridge.LoadAndPlace(path, viewport.MidPoint)` which placed a *second* dead Chat driving the wired pipeline (orphaned red `Prompt Signal` wire).

- **New `GhJsonBridge.LoadAndPlaceAnchored(string path, IGH_Component anchor, Guid placeholderComponentGuid)`** (`Generation/GhJsonBridge.cs`): FromFile → `GhJson.Fix` → find FIRST component whose `ComponentGuid == placeholderComponentGuid` → record its pivot + every connection touching its id (the OTHER endpoint id + param names + direction) → rebuild the `GhJsonDocument` WITHOUT that component and those connections → `Offset = anchorPivot − placeholderPivot`, `AutoOffset=false` (so the graph keeps its relative layout but lands on the live Chat) → `Put` → re-wire each captured connection to the matching **named** param on `anchor`, remapping the other endpoint's id → placed `InstanceGuid` via `PutResult.IdToGuidMapping` (same pattern as `RestoreFeedbackLinks`). No placeholder found → falls back to ordinary placement at the anchor pivot.
- The placeholder key passed is `_component.ComponentGuid` (the Chat type GUID `B7E4B6F2-…`), which matches the preset file's `componentGuid` (Guid compare is case-insensitive).
- **Refactor:** shared the Put tail into `ExecutePut(doc, options, unfixedIssues, Action<PutResult>? afterPlace=null)` and `BuildPutOptions(offset)`; `PlaceDocument` reuses both. The anchored path passes `afterPlace = RewireAnchor`. New helpers: `RewireAnchor`, `FindParam(IGH_DocumentObject, name, bool output)` (component input/output list by full Name, or floating param), `EmptyDocumentResult`, `readonly record struct RewireRequest(int OtherId, string? OtherParamName, string? SlotParamName, bool SlotIsSource)`. Endpoint ids read via `is int` pattern (GhJSON `Id` is nullable/boxed, like `component.Id`).
- **`ChatWindow.HandlePlacePreset`** now: `EnsureComponentPlaced()` → force `_component.Attributes.ExpireLayout()/PerformLayout()` (valid pivot for anchoring) → `LoadAndPlaceAnchored` → `doc.NewSolution(false)` + refresh.
- **`EnsureComponentPlaced()`** (new): drops the Chat if not yet on a canvas, UNgated by provider state (preset placement is an explicit user action), returns the host doc. The actual drop logic was extracted from `MaybePlaceComponent` into **`DropComponent(canvas, doc)`** (shared).

## 2. Full param names on GhJSON-placed components (ExpireLayout)
`ComponentHelpers.ExpandToFullName` set `param.NickName = param.Name` but never invalidated layout. Components placed by GhJSON `Put` were already laid out with the short JSON nicknames, so capsule width stayed narrow and GH truncated the longer full name (`Si…`, `C…`). Fix: added `obj.Attributes?.ExpireLayout()` at the end of `ExpandToFullName`, so the next layout pass (from the following `NewSolution`/refresh) recomputes capsule widths. Only runs when `CentralSettings.CanvasFullNames` is on. Fresh single-object placements (Conversation Log/Picker) were already fine (their first layout used the expanded names).

## 3. Chat window opens centred over the GH editor (multi-monitor)
The window had no position set and was owned by the Rhino main window, so on multi-monitor it opened on Rhino's monitor — off the canvas — which broke the anchored Chat placement (`AnchorRightOfWindow` maps the window's screen rect onto canvas world coords). New Windows-only `ChatWindow.PositionOverGrasshopperEditor()` (called in `Shown`, just before `OwnToGrasshopperEditor()`): reads the GH editor + chat-window rects natively via `GetWindowRect` (device px, consistent with the existing anchor math — no Eto/WPF DPI surprises) and centres the window over the editor with a new `SetWindowPos` P/Invoke (`SWP_NOSIZE|SWP_NOZORDER|SWP_NOACTIVATE`). No-op if either HWND is unavailable (Eto default placement stands). `_ghEditor` is already set by `HookHostClose()` earlier in `Shown`.
