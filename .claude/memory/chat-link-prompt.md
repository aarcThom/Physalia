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
   dead too: chat markdown rendered on browser defaults plus our wrapper — no bullets, no table
   borders, no underline on a link. **Fixed the same day in a second pass** (see below), not as part
   of the bug fix: it restyles every message, which is a different decision from repairing a dialog.
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
- **Untested in the WebView: the Copy link button.** It goes through the existing `UseClipboard`
  hook (`navigator.clipboard.writeText`), which Chromium serves on `file://` — but WebView2 was not
  exercised, and the hook fails silently to a `failure` status, so a dead button would look like a
  button that simply never says "Copied". Open link is the one that matters and does not use it.
- Regression test: `tools/uitest/test_link_prompt.py` (see [[headless-chat-ui-testing]]) clicks the
  masked link and measures the overlay's geometry, the card's computed background, and
  `elementFromPoint` at the card's centre. "The dialog is in the DOM" was true the whole time it was
  unreadable.

## The theme, compiled and re-scaled (second pass, same day)

`@source '../node_modules/streamdown-svelte/dist';` in `app.css` is the whole of the compile half —
Tailwind's documented way to include a library's classes, +27 kB on a 3.5 MB single-file bundle. But
**compiling streamdown's theme is not the same as wanting it**: it is written for a page, not for a
460x620 panel, and shipped as-is it looked worse in two specific ways.

- `h1` is `text-3xl` and `h2` `text-2xl` against 14px body text — a heading shouted across a panel a
  third of the width it was designed for. Re-scaled to `text-lg` / `text-base` / `text-sm` with
  tighter margins.
- `td`/`th` carry `min-w-[200px]`, so a THREE-column table measured 600px and scrolled sideways
  inside a 464px window it could have fitted. `min-w-0 max-w-none px-2 py-1 text-xs` → 433px, no
  scroll.
- Code blocks and table wrappers are bordered cards; they now sit in the window's own `neu-well`.

The overrides ride the `theme` prop in `response.svelte` (merged over `baseTheme="shadcn"` with
tailwind-merge, so a listed utility replaces its counterpart and everything unmentioned ships as-is).
`tools/uitest/test_markdown_styles.py` renders one answer holding every prose shape and measures list
style, table width, cell padding, code background and link decoration — run it before and after any
change here.

**Two traps in writing that test**, both of which read as "the feature is broken" rather than "the
harness is wrong":
- **A ```json fence is not a code block in this UI.** `splitContent` collapses it into a `JsonBlock`,
  and `AssistantTurnGroup` folds everything BEFORE the first JSON block into the collapsed *Thinking*
  section — so a sample built around a JSON fence renders as an empty answer. Use ```python.
- **Scope every query to the answer.** `document.querySelector('h2')` finds the first h2 in the whole
  page, which may be inside a hidden setup surface (width 0, and every measurement meaningless).

Not a regression, checked because it looked like one: reasoning text renders in the same blue as the
answer rather than muted. `text-foreground` was always compiled (our own `message-content` uses it),
and the conversation area re-maps `--foreground` to that blue — so the paragraph class beat the
wrapper's `text-muted-foreground` long before this change.
