---
name: chat-widget
description: Bottom-right GH canvas widget that opens the Physalia chat window
metadata: 
  node_type: memory
  type: project
  originSessionId: 8a5b5fab-9bc8-4be6-975f-d11fee3a88ec
---

Chat launcher **canvas widget** (`src/Physalia.GH/Widgets/ChatWidget.cs`, landed 2026-06-21). A placeholder rounded square pinned bottom-right of the GH canvas, above the compass; click opens the standalone chat window (see [[chat-window.md]]).

- Extends **`Grasshopper.GUI.Widgets.GH_Widget`** (NOT `GH_CanvasWidget_FixedObject` — uniform `Padding` can't stack above the compass independently, so we draw in device space ourselves).
- **Widgets are NOT auto-discovered** (unlike components). You MUST register via a `GH_AssemblyPriority` subclass whose `PriorityLoad()` does `GH_Canvas.WidgetListCreated += handler; return GH_LoadingInstruction.Proceed;` and the handler calls `e.AddWidget(new ChatWidget())` (`GH_CanvasWidgetListEventArgs`). Without this the widget never appears. `ChatWidgetPriority` lives in the same `ChatWidget.cs`. (Pattern confirmed from karamme/Parachute.) Once added, GH lists it in the canvas Widgets menu with a visibility checkbox automatically.
- `GH_CanvasMouseEvent` lives in namespace **`Grasshopper.GUI`** (not `.Canvas`). Widget Render runs UNDER the pan/zoom transform → must `g.ResetTransform()` and draw in device pixels (same as `SerializeWidget`); store the rendered `_frame` rect for `Contains`/`RespondToMouseDown` hit-testing.
- **Visible** is abstract on `GH_Widget`; backed by `Grasshopper.Instances.Settings.GetValue/SetValue("Physalia.ChatWidget.Visible", true)` so it defaults ON and the disable choice persists across sessions.
- **`#if WINDOWS`** (like `SerializeWidget`): the widget API params are WinForms (`MouseButtons`, `ToolStripDropDownMenu`) and `UseWindowsForms` is Windows-TFM-only. Mac gets no widget yet — **Mac Todo**.
- Click → `OpenChat`: find a `Chatbox` in `doc.Objects`; if none, `new Chatbox()` + `CreateAttributes()` + place at `canvas.Viewport.MidPoint` + `doc.AddObject(cb, false)` + `ComponentHelpers.ApplyNickNameDisplay(cb)` (respects the "Draw Full Names" setting — see GhJSON NickName note in MEMORY.md); then `cb.OpenWindow()` (reuses Chatbox's single-window static ownership). Widget is a launcher, not a pipeline participant.
- **Pipeline-wiring readiness**: `PromptPipelineView.IsPipelineReady(source, outputIndex)` = Chatbox→Recorder (output 0) AND Recorder output[1]→a `Reasoner` whose "Model" input has `SourceCount > 0`. `ChatWindow.Tick` pushes `ready` (+ a status naming the missing piece) in `setState`.
- **First-run setup state** (separate from `ready`, landed 2026-06-21): when NO provider is configured (no API key / no Claude CLI on PATH / no llama-server), `needsSetup` is pushed and the Svelte app shows `Setup.svelte` (provider-pick screen). Detection + interactive setup UI are now built — see [[chat-window]]. Real widget SVG art still deferred (placeholder square).
