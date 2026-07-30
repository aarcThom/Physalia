---
name: human-tools-taxonomy-moves-2026-07
description: 2026-07-29 relocation — Tools Present → Grounding ribbon section; /export slash command and the signal-trace canvas widget both became Human Tools (header buttons)
metadata: 
  node_type: memory
  type: project
  originSessionId: 1a753740-865c-4dea-9c38-9d24a4a646fe
  modified: 2026-07-30T05:59:07.681Z
---

2026-07-29: three "where does this functionality live" moves. Nothing behavioural changed inside the features — only their entry points.

1. **Tools Present** (`ToolsInUse`) moved from the **LLM Tools** ribbon section to **Grounding** — subcategory string in the `PhyBase` ctor call, plus `git mv` into `Components/Grounding/`. Rationale: its output is a `GH_Grounding`, so it belongs beside the other grounders even though it scans LLM tool nodes.

2. **`/export` is gone as a slash command.** It is now the **Export Conversation** human tool → a header button in the chat window → `exportconversation` bridge verb → the existing `ChatWindow.HandleExportConversation()`. The `IsExportCommand` intercept in `HandleSubmit` was deleted. Knock-on: `BUILTIN_COMMANDS` in `Composer.svelte` is gone entirely (it held only `'export'`) — **the "/" menu now has no complete-on-their-own commands, every kind drills into a sub-path**, which simplified `availableKinds`/`kindLabel`/`acceptRef`. Leaving an empty `BUILTIN_COMMANDS = []` would have been a TS error (`never[]`.includes(string)), so removal was the right call, not a shortcut.

3. **The signal-trace canvas widget is gone** — deleted `Widgets/SignalTraceWidget.cs` and its `AddWidgets` registration. Replaced by the **Signal Trace** human tool → header button → `opensignaltrace` → `SignalTraceWindow.ShowOrFocus()`. Caveat worth remembering: the user asked for "the signal trace for the current conversation", but `SignalTraceLog` is a **process-wide session log** with no per-conversation scoping — the button is a door onto the one log. Scoping would mean tagging entries by owning Conversation Log, which was not built.

Both new tools are marker records on the `HumanTool` union (`ExportConversationTool`, `SignalTraceTool`) — nothing to configure, so they follow the `AddImage` shape exactly: plain `HumanToolComponentBase`, no context menu, read-only "Enabled" row in the chat's Grounding panel. Full wiring path for any future marker human tool: Core record → component → `ConversationLog.ReadHumanToolInputs` + `Has…Tool` property → `ChatWindow` state push (BOTH the signature and the state JSON, they are two separate anonymous objects on adjacent lines) → `bridge.ts` `UiState` → `App.svelte` state/`setState`/`groundingAvailable`/header button → `Grounding.svelte` props + row.

Unlike the snapshot buttons, these two header buttons are **not** gated on `composerInert` — they read what already happened, so they stay live while the pipeline is busy or a provider is still being set up.

Verified: `dotnet build -c Debug` compiles clean and `npm run check` reports 0 errors (7 pre-existing `state_referenced_locally` warnings in `Grounding.svelte`). The bin copy failed only because Rhino had the .gha loaded — **not yet run live in Rhino**. Related: [[human-tools-split]], [[signal-trace-widget]], [[tools-in-use-component]], [[view-snapshot-human-tool]].
