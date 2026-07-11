---
name: signal-trace-widget
description: Signal Trace debugging widget + window — 3 taps (EmitSignal/MarkConsumed/FeedbackCollector.Inject) feed a GH-side static SignalTraceLog; Eto GridView window; canvas widget
metadata: 
  node_type: memory
  type: project
  originSessionId: 306541bc-0ef9-4671-8f4e-ec93e40c9e78
---

2026-07-10: Signal Trace debugging feature (canvas widget, not a component).

Architecture (zero Core changes — boundary rule preserved):
- `src/Physalia.GH/Diagnostics/SignalTraceLog.cs` — process-wide static registry, lock + 500-entry ring, `Version` counter polled by UI (no cross-thread events). Entries are lightweight snapshots (`SignalTraceEntry.cs`): full payload text capped 64KB, content blocks + Instructions reduced to summaries — NEVER retains PhySignal refs (no image/conversation pinning). Copy-on-write immutable records; consumption appends replace the entry under lock.
- Three taps, complete coverage: `StatefulComponentBase.EmitSignal` (sequence-deduped — latched signals re-emit every solve), `StatefulComponentBase.MarkConsumed` (consumer GUID+name+input name; first-observation baseline bypasses it so baselining ≠ consumption), `FeedbackCollector.Inject` (recorded as "(wireless)" consumption). Direct mints (LlmCall aux, Router, ToolComponentBase, StallGuard, GeometryObservation) all reach EmitSignal — no per-site edits. Capture is inherently gated on Physalia presence (taps live inside component code).
- `src/Physalia.GH/Panels/SignalTraceWindow.cs` — plain Eto Form (cross-platform), singleton `ShowOrFocus()`, GridView (#/Time/Source/Outcome/Payload/Carries/Consumed) + splitter detail pane (full payload, block summaries, consumption timeline), 0.25s UITimer polling Version, Pause (UI-only)/Clear/outcome filter/search.
- `src/Physalia.GH/Widgets/SignalTraceWidget.cs` — `#if WINDOWS` GH_Widget like ChatWidget (drag machinery duplicated, `Physalia.TraceWidget.*` settings keys), 48px GDI pulse glyph, docked above chat widget (right 14 / bottom 200), double-click opens window. Registered via existing `ChatWidgetPriority` (handler renamed `AddWidgets`).

Gotcha: `Diagnostics.SignalTraceLog` in Components namespace resolves via enclosing-namespace lookup (using directives don't import nested namespaces, so `System.Diagnostics` can't collide).

Builds compile clean; live Rhino test pending (plan's verification list: capture-before-open, no dupes on F5, DeconstructSignal adds no consumption, wireless row, ring eviction). Plan: `~/.claude/plans/is-there-a-way-immutable-donut.md`. Related: [[single-signal-output-rework]].
