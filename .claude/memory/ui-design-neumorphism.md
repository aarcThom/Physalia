---
name: ui-design-neumorphism
description: "Physalia chat UI design language — neumorphism (soft UI): one blue base colour, dual shadows, no borders; the --neu-* tokens + .neu-* helpers and their gotchas"
metadata: 
  node_type: memory
  type: project
  originSessionId: 86634752-6e55-43d9-b319-a6cde878994b
---

The Physalia chat window ([[chat-window]], `src/Physalia.UI`) uses a **neumorphic ("soft UI")** design language. Keep new UI consistent with it.

**Core rules:**
- Every surface shares ONE soft-blue base colour (`--neu-bg`); shape comes only from a shadow pair — a near-white highlight top-left + a darker-blue shade bottom-right. **No borders, no rings, no divider lines.**
- Raised = pushes out of the surface; inset/well = presses in (text inputs, code/JSON blocks). Buttons are raised, lift on hover, press in (inset) on active/open.
- The page background is the FLAT base colour (no gradient) so same-colour surfaces blend and read from their shadows. One accent colour (`--neu-accent`) only, for the primary CTA (Send).

**Implementation (single source of truth = `src/Physalia.UI/src/app.css`):**
- Tokens in `:root`: `--neu-bg`, `--neu-light`, `--neu-dark`, `--neu-accent`, and shadow values `--neu-shadow`, `--neu-shadow-sm`, `--neu-inset`, `--neu-inset-sm`.
- `@layer components` helpers: `.neu-raised` / `.neu-raised-sm` (raised), `.neu-well` (inset), `.neu-btn` (pressable raised→inset), `.neu-ghost` (flat until hover). Apply via these classes, never one-off box-shadows. Shadcn `button`/`badge` variants are rewired to them; utilities still win for accent/destructive fills.
- Light `::-webkit-scrollbar` styling (WebView2 is Chromium) — the default rendered dark.

**Two non-obvious constraints (cost two rounds of feedback):**
1. **Shadow extent must stay under the scroll gutter.** The chat scrolls in a `p-4` (16px) container and `overflow-y-auto` forces the x-axis to clip, so a shadow reaching >16px gets hard-clipped at the window edge on full-width blocks (reads as an outline). Extents are kept at 14px / 10px on purpose — don't grow them.
2. **Never put a neu-shadowed element as a child of an `overflow-hidden` parent.** A parent's `overflow-hidden` clips a CHILD's box-shadow (but NOT its own). This is exactly why full-width JSON/tool cards looked flat-edged while user bubbles (shadow on the element itself) were fine — fixed by removing `overflow-hidden` from `message-content`.

Build/stage after edits per [[physalia-repo-gotchas]] (`dotnet build`, not `npm run build` alone — now a MUST DO in CLAUDE.md).
