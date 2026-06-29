# Chat Window — standalone Svelte chat driving the GH pipeline

## Status: IMPLEMENTED (2026-06-20)

The full Svelte UI is built and wired. What shipped (differs from the spike-era notes below
in a few specifics — those are kept for rationale):

- **New project `src/Physalia.UI`** (in `Physalia.slnx`): Vite + Svelte 5 + TypeScript +
  Tailwind v4 + **shadcn-svelte** with the **svelte-ai-elements** registry components
  (`conversation`, `message`, `response`, `chain-of-thought`, `tool`, `prompt-input`, `image`,
  plus their shadcn bases). It's an MSBuild wrapper (compiles no C#); the npm/Vite build runs
  only with `-p:BuildUI=true` (or `npm run build`), so a plain `dotnet build` needs no Node.
- **Single-file output.** `vite-plugin-singlefile` inlines all JS/CSS into one `index.html`
  (`src/Physalia.UI/dist/`), **embedded into the `Physalia.GH` assembly** as the
  `Physalia.GH.chat.html` manifest resource by the `EmbedChatHtml` target (it is NOT placed in
  `Files/`, which is reserved for user-alterable content). At runtime `ChatWindow` extracts it to
  a temp file and loads it via `file://` — singlefile means no cross-origin module fetches, so
  `file://` works and dodges the WebView2 `NavigateToString` size limit.
- **Bundle trimming (16 MB → 3.4 MB).** streamdown pulls Shiki (all langs), mermaid (+cytoscape)
  and katex by default; singlefile inlines them all. `vite.config.ts` aliases unused Shiki
  grammars (keep `json`), all but github light/dark themes, and `mermaid`/`katex` to empty stubs
  (`src/lib/shiki-empty-lang.ts`, `src/lib/empty-module.ts`) — none are reachable since the chat
  enables no math/diagram plugins.
- **Light mode** is forced: no `ModeWatcher` is mounted and `.dark` is never applied; `app.css`
  sets `color-scheme: light`.
- **Thinking → ChainOfThought**, **tool calls → Tool.** The Core conversation has no reasoning
  channel, so `<think>…</think>` is parsed out of assistant text in the UI (`splitThinking`).
  Tool calls are surfaced from `ToolCallContent` (+ the matching `ToolResultContent`) into the
  Tool component (state input-available / output-available / output-error).
- **Images:** paste into the textarea or drag-drop onto the window (prompt-input handles both) →
  `FileUIPart` data URLs → C# decodes to `InlineImage` content blocks via `SubmitFromWindow`.

### Final bridge contract (`src/Physalia.UI/src/lib/bridge.ts` ↔ `ChatWindow.cs`)
- **C# → JS** (ExecuteScript on a 0.1 s UITimer, change-detected): `window.physalia.setHistory(UiMessage[])`,
  `setStream(text|null)`, `setState({connected,busy,status})`.
- **JS → C#**: page stashes `JSON.stringify({text,images})` on `window.__physaliaPending`, navigates
  `phbridge://submit`; `ChatWindow` cancels the navigation (synchronously) and, deferred via
  `Application.Instance.AsyncInvoke`, pulls it back with `__physaliaTake()` and calls
  `Chatbox.SubmitFromWindow(text, contentBlocks)`. Payload rides on `window`, not the URL, because
  base64 images exceed URL limits.

### Still open / Mac
- Live in-Rhino test (history/stream/tool/image render; classic Prompter regression) — pending.
- Reasoning only appears for models that inline `<think>` in their text; a dedicated Core reasoning
  channel would make it universal.
- Mac: verify Eto picks WKWebView and the bridge + `file://` load behave (see Mac TODO below).

---

## Context

The classic **Prompter** is a canvas-panel chat: it paints the conversation on the GH
canvas and accepts prompts via a double-click in-place TextBox. It works, but a
node-on-canvas chat caps how polished the UX can feel next to competitors.

We want a **second, additive** entry point: a standalone window hosting a modern
Svelte chat UI (the look of https://svelte-ai-elements.vercel.app/), driven by a tiny
GH component that sits where Prompter sits in the pipeline. **Prompter is NOT removed** —
both are offered. The classic Prompter keeps its canvas panel and its `Image Sources`
input / `/<alias>` referencing; the new component handles images in-window (paste/drop).

Mac support is coming soon, so the window host is chosen for cross-platform ease.

## Why this is small

The classic Prompter is already two loosely-coupled halves:

- **Component (`Prompter.cs`)** — a `StatefulComponentBase` source. The entire send path is
  `SubmitUserMessage()`: `LatchSuccess(text, contentBlocks)` + `ExpireSolution(true)`. It owns
  no conversation state.
- **UI (`PrompterAttrib.cs`)** — gets the conversation and live stream **by walking the wire
  graph, not through any input**:
  - `FindRecorder()` → `Params.Output[0].Recipients` → the wired Recorder → `ActiveConversation`.
  - `GetStreamingText()` → Recorder's `Output[1].Recipients` → an `IStreamingTextSource` that
    `IsBusy` → `.StreamingText`.
  - `IsPipelineBusy(recorder)` → busy state for the input gate.

So the response/streaming channel is **pure graph traversal**. The new component reuses the
same traversal and pushes results to a window instead of painting them. No new pipeline plumbing.

## Decision: Eto.Forms `WebView`

One `WebView` class for both platforms (Mac priority):

- **Mac** → Eto backs `WebView` with **WKWebView** (modern Chromium-class, renders Svelte fine).
- **Windows** → Eto's **WebView2** backend (must be confirmed active — see Spike).
- Same C# API and same bridge on both. Already depend on Rhino-shipped **Eto 2.11** (Manage
  Images dialog) — no new dependency.
- Bridge has no native `postMessage`: JS→C# via custom-URI navigation intercept; C#→JS via
  `ExecuteScript`. Clunkier than WebView2-direct, but **identical on Mac and Windows**, which is
  the whole point.

WebView2-direct was rejected: Windows-only, would force a from-scratch Mac rewrite (WKWebView +
different bridge) later.

## Phase 0 — De-risk spike (do this FIRST)

Before any real code, prove the host works on the dev's Rhino-shipped Eto 2.11:

1. Eto `Form` + `WebView`, load a throwaway Vite/Svelte "hello world" build.
2. Confirm it renders in **Chromium (WebView2)**, NOT IE/Trident. (If IE, wire up the WebView2
   backend or stop and reassess — this is the make-or-break.)
3. Round-trip one message each way: JS→C# (custom-URI intercept) and C#→JS (`ExecuteScript`).

If this passes, everything below is mechanical.

## Components

### New component — `Chatbox` (nickname "Chat")
- `StatefulComponentBase`, **no inputs**, one output `Prompt Signal` (item) — wire to Recorder's
  Prompt Signal input, exactly like Prompter.
- `SolveInstance`: `EmitSignal(DA, 0, SuccessSignal)` (same pattern as Prompter).
- Owns one window instance (open via double-click / context menu; window holds a back-reference
  to the component). Dispose the window on component removal / document close.
- `SubmitUserMessage(text, blocks)` — the JS→C# sink: `LatchSuccess(text, contentBlocks: blocks)`
  + `ExpireSolution(true)`, marshalled onto the GH main thread.
- Reads conversation + stream via the shared traversal helper (below) and pushes to the window.

### Classic `Prompter` — unchanged
Stays as-is: canvas panel, `Image Sources` input, `/<alias>` resolution via `PromptImageResolver`.
Both components coexist in the Core tab.

### Shared traversal helper (refactor)
`PrompterAttrib` currently has `FindRecorder` / `GetStreamingText` / `IsPipelineBusy` as private
methods. Lift the graph-traversal core into a small shared static helper (e.g.
`Components/Core/PromptPipelineView.cs`) taking the source component + its output index, so both
`PrompterAttrib` and the new `Chatbox` window controller use one implementation. Keep behaviour
identical; this is a pure extract-and-reuse, no logic change.

## Window + bridge protocol

Small JSON message contract over the Eto WebView bridge:

- **JS → C#** (custom-URI intercept): `{ "type": "submit", "text": "...", "images": [...] }`
  → `Chatbox.SubmitUserMessage`. `images` are base64/data-URI from paste/drop → `ImageContent`
  blocks (`InlineImage`), reusing existing `MessageContent` types.
- **C# → JS** (`ExecuteScript`):
  - `{ "type": "history", "messages": [...] }` — from `ActiveConversation`, pushed each solve.
  - `{ "type": "streamDelta", "text": "..." }` / `{ "type": "streamEnd" }` — from the
    `StreamingText` poll while busy (the existing animation timer cadence can drive the poll).
  - `{ "type": "busy", "value": true|false }` — from `IsPipelineBusy`, gates the input.

## Threading rules (the one place this bites)

Three threads: window UI thread ≠ GH main thread ≠ Reasoner background thread.

- Submit arrives on the window thread → marshal to the GH main thread before touching the
  component (or just call `ScheduleStateSolve`, already background-safe).
- Streaming deltas push to the WebView on **its** UI thread (`Application.Instance.AsyncInvoke`).
- `AddRuntimeMessage` only inside `SolveInstance`, never from the bridge (existing GH async rule).

## Images — both paths offered

- Classic Prompter: `Image Sources` input + `/<alias>` (unchanged).
- New Chatbox: paste/drop in the window → inline `ImageContent` blocks via the same
  `LatchSuccess(..., contentBlocks:)` sink. No GH input wire.

## Svelte build / bundling

- Svelte + Vite build → static assets shipped next to the `.gha`; served to the WebView from a
  local folder (or loaded as a packaged resource).
- **Cost to accept:** this adds a Node/Vite toolchain to a C#-only repo (a second build step in
  CI). Keep the Svelte app in its own subfolder with its own `package.json`; the .NET build copies
  the built `dist/` next to the `.gha` (post-build step), like the GhJSON DLL copy.

## Mac TODO

- Confirm Eto picks WKWebView on Rhino Mac and the custom-URI intercept + `ExecuteScript` bridge
  behave there (the WinForms-free path should, but untested — same caution as PrompterAttrib's
  Mac note).
- Verify the standalone `Eto.Forms.Form` lifecycle (show/close/dispose tied to component) on Mac.

## Verification (end to end)

1. Place `Chatbox` → Recorder → Reasoner (with a configured model). No inputs on Chatbox.
2. Open the window (double-click), type a prompt, send → assistant response **streams** into the
   window live, then the committed turn appears in history.
3. Paste an image into the window + text → multimodal turn reaches the model (assistant responds
   about the image).
4. Classic Prompter still works unchanged on the same canvas (regression check).
5. Close/reopen the document → window disposes cleanly, no leaked process/handle.
