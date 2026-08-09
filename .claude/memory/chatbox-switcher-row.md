---
name: Chat-switcher-row
description: Chat window bottom row of circles switches the single window between multiple Chat components
metadata: 
  node_type: memory
  type: project
  originSessionId: 12e9e309-0577-4feb-9434-943d7781d938
---

The single session-wide chat window can now switch which Chat component it views. A row of
circles at the very bottom of the window (`App.svelte`, after the System Prompt) shows one dot per
Chat on the canvas; clicking a dot views that component's Conversation Log history (or the default
ConnectOptions screen when it has none). Built 2026-06-23, builds clean, live Rhino test pending.

**How it works:**
- `ChatWindow._component` became MUTABLE (was readonly) — the currently *viewed* Chat.
  `SetActiveComponent(Chat)` rebinds it and calls `ResetPushedState()` (nulls the per-component
  `_lastConversation/_lastStream/_lastConnected/_lastBusy/_lastReady/_lastNeedsSetup/_lastStatus/_lastConfigured`
  caches) so the new component's history/state re-pushes fresh next tick. Preset + Chat-list
  signatures are global, left intact.
- `Tick` → `MaybePushChats()` serializes `EnumerateChats()` → `{id (InstanceGuid), active, hasHistory,
  emoji, harness}[]` to `window.physalia.setChats`, change-detected via `_lastChats`. `EnumerateChats`
  walks `PhyDocuments.ObjectsIncludingHarnesses(host)` and always includes `_component` even when
  detached (widget-created, awaiting placement). `hasHistory` = wired Conversation Log's
  `ActiveConversation` has Messages.
- **Home leads the row (2026-08-09).** A house icon (lucide `house`), always first, always ruled off
  from the chats by the divider, sentinel id AND harness key `"home"`. It is **not a Chat** — a
  `bool _home` on `ChatWindow` — so it survives every harness being deleted and needs no backing
  component. `Tick` forces `conversationLog = null` while home, which is the whole mechanism: every
  field downstream already copes with an unwired Chat, so the page falls back to the ConnectOptions
  screen and the composer greys out (its inert gate keys on `connected`). `HandleSubmit` returns
  early on home as well — `_component` is still whatever Chat was last viewed, so a send would
  otherwise post into a conversation the user cannot see. `SetActiveComponent` treats leaving home as
  a switch even when the component is unchanged; `OnComponentRemoved` falls back to `ShowHome()`
  instead of closing the window. `EnumerateChats` no longer force-inserts a detached `_component` —
  Home stands in its place, so a not-yet-placed Chat never gets a circle.
- **Home vs an empty harness — same surface, different content.** Both show `ConnectOptions` (neither
  has a Conversation Log, so `showConnect` is true for both). `UiState.home` tells them apart: Home
  gets the three pills, a placed Chat still awaiting its Conversation Log gets **the logo alone** —
  the user has already chosen their harness and is inside it, so offering to place another answers a
  question they did not ask. The status line ("Wire a Conversation Log to this Chat to begin.") is
  what directs them.
- **Which entry point lands where:** the canvas widget → `OpenWindow(home: true)` → **always Home**,
  whether or not harnesses exist (the widget is the door back to placement). Double-clicking a
  harness → `HarnessAttrib` → `FindChat()` → `OpenWindow()` → **that harness's first Chat**.
  `selectChat` in `App.svelte` also clears `panel`/`manualSetup`, because those pages render in front
  of the conversation and Home would otherwise appear to do nothing.
- **Grouped by harness, with a divider between groups (2026-08-09, once a document could hold many
  harnesses).** `CompareChats` sorts on the owning harness proxy's pivot on the HOST canvas first,
  then the Chat's own pivot inside it. Sorting on the Chat's pivot alone — what it used to do —
  interleaves harnesses, because **every harness sub-document has its own coordinate space**: two
  Chats at the same spot in different harnesses are indistinguishable by position, and a preset's
  Chat at (0,0) sorts ahead of everything regardless of where its proxy sits. Harness-less Chats
  (loose in a pre-harness file, or detached) key on `PointF(-∞,-∞)` so they band together at the
  left rather than scattering. The `harness` field (proxy InstanceGuid, `''` for none) is what the
  row compares against the previous entry to rule a `w-px` divider — so a divider can only ever fall
  on a boundary, and a single-harness file never draws one.
- Bridge `phbridge://selectChat?id=<guid>` → `HandleSelectChat` → `SetActiveComponent`.
- `Chat` lost the static `_activeOwner`; `OpenWindow` now `existing.SetActiveComponent(this)` +
  BringToFront instead of close/reopen, so double-clicking another Chat switches the view.
  `RemovedFromDocument` → `_activeWindow?.OnComponentRemoved(this)`: if the viewed one was deleted,
  switch to another Chat on the canvas, else `Close()`.
- UI: `UiChat` type + `setChats` in [[chat-window]]'s bridge.ts; circle = accent dot (active) /
  filled grey (hasHistory) / inset well (empty), `cn`-composed in App.svelte.

Related: [[chat-widget]], [[chat-window]].
