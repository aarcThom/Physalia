---
name: ghjsonbridge-facade
description: "GhJsonBridge façade location and the placement nuggets around it — nickName round-trip, the Put-mutates-live-doc deferral, and the canvas HUD transform trick"
metadata: 
  node_type: memory
  type: project
  originSessionId: 9070a607-d646-44b4-a78e-a43dc097a34d
  modified: 2026-07-26T05:04:44.634Z
---

All GhJSON library calls funnel through `internal static class GhJsonBridge` — at **`Physalia.GH/Generation/GhJsonBridge.cs`, namespace `Physalia.GH.Generation`** (moved from the old `GhJSON` folder). It is now split across several partial files (`.CanvasState`, `.Patch`, `.Lint`, `.Validity`, `.Fidelity`, `.Internalize`). **CLAUDE.md and the code are authoritative for its current API and the `PlaceResult` shape**, which has grown well beyond the original 5-field record.

- **NickName round-trip:** GhJSON stores `nickName` only when `!= Name`; export `StripNickNames` nulls them so files carry only full `parameterName`. Import `ComponentHelpers.ApplyNickNameDisplay(PutResult.PlacedObjects)` is **setting-aware** — sets `NickName = Name` + `ExpireLayout` (see [[chat-window-placement-fixes]]) only when `Grasshopper.CentralSettings.CanvasFullNames` is on, else leaves abbreviations (a later toggle is handled by GH's own doc-wide conversion). Applied at ALL Physalia programmatic placements (GhJSON import, ChatWidget's Chatbox [[chat-widget]], `PickerAdd`, `PythonShortcut`). Router's dynamic "T1" nicknames are functional labels — NOT touched. It also must not clobber floating-param nicknames ([[slider-nicknames]]).
- **Placer pattern:** `GhJsonGrasshopper.Put` mutates the live doc AND calls `NewSolution(true)` internally — defer via a one-shot `Rhino.RhinoApp.Idle`. Place beside a component via explicit `PutOptions.Offset` + `AutoOffset=false`.
- **Canvas HUD nugget:** a persistent on-canvas HUD during an interaction (e.g. Serializer's "select objects then Enter" banner) draws via `GH_Canvas.CanvasPostPaintWidgets += …` (subscribe on interaction start, unsubscribe + `canvas.Refresh()` on end). It paints UNDER the pan/zoom transform — to pin it to a window corner, save `g.Transform`, call `g.ResetTransform()`, draw in device px, then restore (dispose the saved Matrix). Without the reset the banner lands far out in world space.
- **Serializer** (`Components/Extra/` — the folder was renamed from `Serializers/`) interactive export is Windows-only; **Deserializer** is cross-platform ([[mac-todo]]). Both delegate here.

Presets are no longer GhJSON at all — see [[harness-subdocument]]. Patch-mode specifics: [[iterative-canvas-editing]], [[grouping-and-panel-placement-fixes]].
