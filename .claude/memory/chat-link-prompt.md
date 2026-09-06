---
name: chat-link-prompt
description: Why the chat's external-link confirmation is ours and not streamdown's — Tailwind never scans node_modules, and window.open reaches no browser inside the Eto WebView.
metadata:
  type: project
---

**2026-09-05, fixed and verified headlessly (not yet in Rhino):** clicking a link inside a model's
answer opened a warning screen with no backdrop, no card and no positioning — its text laid straight
over the conversation, both unreadable. Two independent causes, and either alone would have broken it.

1. **Tailwind never scans `node_modules`.** `streamdown-svelte`'s `LinkSafetyModal.svelte` is styled
   with `fixed inset-0 bg-black/45 backdrop-blur-sm max-w-md bg-white shadow-xl …`, and none of those
   are compiled into our bundle unless a class is ALSO used in `src/`. The ones that happened to
   overlap (`z-50`, `inset-0`) were present, which is exactly why it looked like a half-broken dialog
   rather than an unstyled one. **This is not confined to the modal** — streamdown's whole `shadcn`
   theme lives in the same package, so `list-decimal`, `border-collapse`, `underline` and the rest are
   dead too: chat markdown renders on browser defaults plus our wrapper. A one-line
   `@source '../node_modules/streamdown-svelte/dist';` in `app.css` would compile all of it, at the
   cost of restyling every message — deliberately NOT done as part of the bug fix.
2. **`window.open` reaches no browser here.** Streamdown's confirm calls
   `window.open(href, '_blank')`; the chat is a `file://` page in an Eto WebView with no
   `NewWindowRequested` handling, and the host shells out only for `phbridge://open?url=`
   (`ChatWindow.HandleOpen` → `Process.Start`). So a *correctly styled* streamdown modal would still
   have had a dead button — or navigated the whole UI away from `file://`.

**The fix:** `src/lib/chat/LinkPrompt.svelte`, handed to Streamdown as
`linkSafety={{ enabled: true, renderModal: linkPrompt }}` from `response.svelte`. Styled with the
app's own `neu-*` classes (which Tailwind does scan) and confirming through the new
`openExternalLink()` in `$lib/bridge` — the same path `Setup.svelte`'s links already took, now shared
(App.svelte's `BRIDGE_SCHEME` moved there too).

- **The dialog earns its place beyond the styling.** A markdown link shows the model's LABEL, not the
  destination: the report that started this said "download zipped LAS" over a
  `webtransfer.vancouver.ca` URL. Disabling `linkSafety` and letting the anchor through would have
  been fewer lines and worse.
- Streamdown renders the custom modal **per intercepted link**, mostly with `isOpen: false`, so the
  component must render nothing when closed and mount its Escape listener only while open.
- A `renderModal` typed `(props) => unknown` accepts a Svelte snippet — `svelte-check` is happy.
- Regression test: `tools/uitest/test_link_prompt.py` (see [[headless-chat-ui-testing]]) clicks the
  masked link and measures the overlay's geometry, the card's computed background, and
  `elementFromPoint` at the card's centre. "The dialog is in the DOM" was true the whole time it was
  unreadable.
