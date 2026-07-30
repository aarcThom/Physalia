---
name: signal-trace-widget
description: Signal Trace debugging window — 3 taps (EmitSignal/MarkConsumed/FeedbackCollector.Inject) feed a GH-side static SignalTraceLog; Eto GridView window (opened from the chat window since 2026-07-29; the canvas widget is gone)
metadata: 
  node_type: memory
  type: project
  originSessionId: 306541bc-0ef9-4671-8f4e-ec93e40c9e78
  modified: 2026-07-30T05:58:47.693Z
---

2026-07-10: Signal Trace debugging feature (canvas widget, not a component).

Architecture (zero Core changes — boundary rule preserved):
- `src/Physalia.GH/Diagnostics/SignalTraceLog.cs` — process-wide static registry, lock + 500-entry ring, `Version` counter polled by UI (no cross-thread events). Entries are lightweight snapshots (`SignalTraceEntry.cs`): full payload text capped 64KB, content blocks + Instructions reduced to summaries — NEVER retains PhySignal refs (no image/conversation pinning). Copy-on-write immutable records; consumption appends replace the entry under lock.
- Three taps, complete coverage: `StatefulComponentBase.EmitSignal` (sequence-deduped — latched signals re-emit every solve), `StatefulComponentBase.MarkConsumed` (consumer GUID+name+input name; first-observation baseline bypasses it so baselining ≠ consumption), `FeedbackCollector.Inject` (recorded as "(wireless)" consumption). Direct mints (LlmCall aux, Router, ToolComponentBase, StallGuard, GeometryObservation) all reach EmitSignal — no per-site edits. Capture is inherently gated on Physalia presence (taps live inside component code).
- `src/Physalia.GH/Panels/SignalTraceWindow.cs` — plain Eto Form (cross-platform), singleton `ShowOrFocus()`, GridView (#/Time/Source/Outcome/Payload/Carries/Consumed/Shown) + splitter detail pane (full payload, block summaries, consumption timeline), 0.25s UITimer polling combined Version, Pause (UI-only)/Record Messages toggle/Clear/outcome filter (signals only)/search; top-right **Export Transcript** button writes the FULL merged unfiltered log to .txt.
- `src/Physalia.GH/Diagnostics/RuntimeMessageTrace.cs` + `MessageTraceEntry.cs` — opt-in (toggle, process-static so it survives window close) recorder of Error/Warning runtime messages on StatefulComponentBase components. Samples presence at every `GH_Document.SolutionEnd` (follows doc switches via `ActiveCanvas.DocumentChanged`); open/close diffing keyed by component|level|text gives each message its actual display window — transient mid-burst flashes record ~tens of ms, recognizably ignorable. Message rows intersperse the timeline, tinted (pale red = error, pale amber = warning), duration in the Shown column.
- **Opener, since 2026-07-29:** the Signal Trace *human tool* (`Components/HumanTools/SignalTrace.cs`) → a header button in the chat window → `opensignaltrace` bridge verb → `SignalTraceWindow.ShowOrFocus()`. `Widgets/SignalTraceWidget.cs` (the old `#if WINDOWS` GH_Widget with the GDI pulse glyph) was DELETED and unregistered from `ChatWidgetPriority.AddWidgets`. See [[human-tools-taxonomy-moves-2026-07]].

Gotcha: `Diagnostics.SignalTraceLog` in Components namespace resolves via enclosing-namespace lookup (using directives don't import nested namespaces, so `System.Diagnostics` can't collide).

Builds compile clean; live Rhino test pending (plan's verification list: capture-before-open, no dupes on F5, DeconstructSignal adds no consumption, wireless row, ring eviction). Plan: `~/.claude/plans/is-there-a-way-immutable-donut.md`. Related: [[single-signal-output-rework]].
