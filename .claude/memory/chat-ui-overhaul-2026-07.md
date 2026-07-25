---
name: chat-ui-overhaul-2026-07
description: "Chat window layout overhaul (2026-07-24/25) — top-row human tools, right action stack pinned to prompt box, recessed always-visible scrollbar, fade edges, status row removed into placeholder"
metadata: 
  node_type: memory
  type: project
  originSessionId: b671f565-30c7-4758-ae14-a58dfcba49a8
---

Chat window UI overhaul, iterated over ~8 annotated-screenshot rounds (2026-07-24/25). Final state:

- **Header row**: menu button, then human tools (Add Image, Geometry Snapshot) spreading rightwards as wired. Grounding & tool options moved into the menu dropdown. "Physalia Chat" label + header clear-all button deleted.
- **Composer** ([[human-tools-split]] follow-on): editor-only well; exports `submit()`/`openPicker()` driven from App via `bind:this`. Bottom row = prompt box + action stack (trash/octagon/arrow), `items-stretch justify-between gap-3` pins stack ends to box edges.
- **Scrollbar** (`.chat-scroll` in app.css): 36px recessed channel (`overflow-y-scroll`, always visible), `margin-block: 16px` to span the content area; thumb = neu-bg rounded rect (border-radius 12px renders 8px visible — 4px transparent border eats radius), inset emboss lighting (light top-left / dark bottom-right; outer shadows barely render on webkit thumbs). Conversation wrapper `pr-3` centres channel on the 36px buttons below.
- **Fade edges**: `.chat-fade-top/-bottom` overlay strips (h-8, first 20% solid). Hand-rolled gradients fading --neu-bg → same colour at alpha 0 (relative colour syntax) — Tailwind v4 `to-transparent` interpolates in oklab and painted a darker seam.
- **Status row REMOVED**: host status strings ("connect a Conversation Log…", "add an LLM Call…") render as the composer placeholder; placeholder is deliberately blank while busy ("Working…" unreachable).
- **Text containment**: bubble text `break-words hyphens-auto`; ConversationContent `overflow-x-hidden` (horizontal scrollbar must never appear).

**Why:** Thomas iterates visually — sends annotated screenshots, expects pixel-level fixes; pixel-sampling the screenshot (System.Drawing crop/GetPixel) twice found root causes reasoning alone missed.

**How to apply:** when chat-UI visual bugs come in, crop + sample the screenshot before theorising; remember the webkit-scrollbar constraints (no thumb margin except transparent border + background-clip; border-radius on border box; outer shadows unreliable) and the oklab/transparent gradient seam gotcha.

Builds clean (UI + slnx Debug, 0 errors), live test pending.
