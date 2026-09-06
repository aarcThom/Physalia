# UI test harness

Drives the built chat window (`src/Physalia.UI/dist/index.html`) in headless Chrome, with no Rhino
and no Eto WebView. The bundle is one self-contained HTML file, so the host side is just a stub:
`build_preview.py` copies it and injects a `<script>` that installs a fake `window.physalia`, pushes
a full `UiState`, and opens the image editor on a synthetic capture.

```
python tools/uitest/build_preview.py out.html          # stub only
python tools/uitest/test_all_tools.py file:///…/out.html shot.png
python tools/uitest/test_text_canvas.py file:///…/out.html shot.png
python tools/uitest/test_static_surface_layout.py out.html   # then --dump-dom for data-diag-*
python tools/uitest/test_page_chrome.py out.html shot.png    # drives itself over CDP
python tools/uitest/test_link_prompt.py out.html shot.png    # clicks a link in an answer
python tools/uitest/test_provider_edit.py out.html           # then --dump-dom for data-diag-*
```

`test_static_surface_layout.py` and `test_page_chrome.py` measure the window's chrome AROUND a
page rather than the page itself: that the prompt box and its action stack are absent wherever
there is nothing to send a message to, that the page's scroller then reaches the bottom of the
window, and that the back control is a raised button. Drive them at 460x620 — `ChatWindow`'s real
client size — or the layout fits and says nothing.

`test_provider_edit.py` walks the setup page's CONFIGURED-provider path: that a ready provider's
pill is a BUTTON at all (it was a plain label, which left the connected footer written and
unreachable), that opening one prefills the URL box from the endpoint in effect rather than the
catalog default, that the key box is blank and says the saved key is kept, and that Disconnect
asks twice for a stored key but goes straight through for Claude Code, which stores none. It
also reports `scrollWidth` vs `clientWidth` per phase — a screenshot at a size the headless
layout viewport does not match will LOOK clipped when nothing overflows.

`test_link_prompt.py` clicks a MASKED markdown link in an assistant turn and measures the
confirmation that comes up: that its overlay is fixed and covers the window, that the card is
opaque and that `elementFromPoint` at the card's centre lands inside the card. The bug it covers
rendered that dialog with none of its styling, so its text lay over the conversation and both were
unreadable — "the dialog is in the DOM" was true throughout.

`cdp.py` is a minimal Chrome DevTools Protocol client (hand-rolled WebSocket frames — there is no
websocket library installed here) used to inject **trusted** input. That matters: a synthetic
`dispatchEvent(new PointerEvent(...))` runs no default action, so it cannot reproduce or disprove
anything about focus, selection or compatibility mouse events. A synthetic-event harness passed the
image editor's text tool while it was broken in Rhino's WebView.

Assertions read **canvas pixels** (`getImageData`, counting the mark-up colour) rather than DOM
elements, so they say what the user would see and survive the feature being rebuilt underneath.

Two traps, both of which cost a wrong diagnosis:

- `</body>` appears inside the inlined app JS as well as at the end of the document. Injecting with
  a global string replace corrupts the bundle; use `rpartition`.
- Never assert on the DOM in the same tick as the click that changes it — Svelte has not flushed yet.
