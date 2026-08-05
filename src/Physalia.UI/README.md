# Physalia.UI

The chat front-end for Physalia, built with **Svelte 5**, **TypeScript**, **Vite**,
and **Tailwind CSS v4**.

This is a plain Vite + Svelte app (not SvelteKit). It builds to a **single,
self-contained `index.html`** that the Grasshopper plugin (`Physalia.GH`) loads
inside an Eto `WebView` from disk. There is no web server at runtime — the C# host
and this page talk to each other directly (see [The bridge](#the-bridge)).

## Commands

```bash
npm install      # install dependencies
npm run dev      # local dev server with hot reload (browser preview)
npm run build    # produce dist/index.html (one inlined file) for the host
npm run check    # type-check the Svelte + TS sources
```

`npm run dev` runs the UI in a normal browser, which is the easiest way to work on
layout and styling. The host bridge isn't present there, so `window.physalia` is
undefined and the chat stays empty — that's expected.

## How it fits together

The app is small. Start at these three files and follow the imports:

| File | What it does |
| --- | --- |
| `src/main.ts` | Entry point — mounts `App.svelte` into `index.html`. |
| `src/App.svelte` | The whole screen: header, conversation list, and the composer. Holds the top-level state and wires the bridge. |
| `src/lib/bridge.ts` | The **contract** with the C# host: the message/state types and the JS↔C# messaging. |

### The bridge

`Physalia.UI` never makes network calls itself. The C# host
(`Physalia.GH/Panels/ChatWindow.cs`) drives everything:

- **C# → JS:** the host calls `window.physalia.{setHistory,setStream,setState,setSetupResult}`
  to push conversation history, the live streaming text, and connection state into the page.
- **JS → C#:** the page sends an outgoing message by navigating to a `phbridge://…`
  URL that the host intercepts and cancels. Image-bearing messages are too large for
  a URL, so the payload is handed over the WebView message channel (or stashed on
  `window` for the host to pull back).

Everything the two sides agree on — the message shapes and these functions — lives
in `bridge.ts`, with comments explaining each piece.

## Directory map

```
src/
├─ main.ts            entry point
├─ App.svelte         top-level screen + state + bridge wiring
├─ app.css            Tailwind theme (light-mode only)
└─ lib/
   ├─ bridge.ts       host contract: message/state types + JS↔C# messaging
   ├─ content.ts      pure helpers: split assistant text into reasoning / prose / JSON
   ├─ utils.ts        the `cn()` class-name helper + a few type helpers
   ├─ chat/           ← the app's own components (this is the code to read first)
   │  ├─ Composer.svelte          the prompt box (typing, image paste/drop, send)
   │  ├─ AssistantTurnGroup.svelte renders one assistant turn (reasoning, tools, prose, JSON)
   │  ├─ JsonBlock.svelte         a collapsed, copyable JSON panel
   │  ├─ Setup.svelte             first-run / add-a-provider screen
   │  ├─ ConnectOptions.svelte    "connect a recorder" screen
   │  ├─ Pill.svelte              the shared Physalia "pill" style
   │  ├─ HappyFace.svelte         the Physalia critter logo (inlined from Images/phy_critter.svg)
   │  └─ providers.ts             the LLM providers + their setup guides (data)
   ├─ hooks/          a small reusable Svelte hook (clipboard copy state)
   └─ components/     ← VENDORED UI libraries — not authored here
      ├─ ai-elements/ chat building blocks (message, conversation, tool, code, …)
      └─ ui/          shadcn-svelte primitives (button, dropdown-menu, collapsible, …)
```

**Where to focus:** `src/App.svelte`, `src/lib/*.ts`, and `src/lib/chat/` are the
app's own code. The `components/ai-elements` and `components/ui` folders are
third-party component libraries copied in from the
[shadcn-svelte](https://shadcn-svelte.com/) and
[AI Elements](https://ai-sdk.dev/elements) registries — they're meant to be
re-synced from those registries, so they're left as-is rather than rewritten.

## Build notes

The single-file build is handled by `vite-plugin-singlefile`. To keep that one file
small, `vite.config.ts` stubs out heavy optional dependencies that the chat never
actually uses (Mermaid diagrams, KaTeX math, and every Shiki syntax grammar except
JSON) via the small `empty-module.ts` / `shiki-empty-lang.ts` shims. The comments in
`vite.config.ts` explain each alias.

> Wrapper note: `Physalia.UI.csproj` exists only so the .NET build can run
> `npm run build` and copy the output alongside the plugin. There is no C# code here.
