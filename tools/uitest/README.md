# UI test harness

Drives the built chat window (`src/Physalia.UI/dist/index.html`) in headless Chrome, with no Rhino
and no Eto WebView. The bundle is one self-contained HTML file, so the host side is just a stub:
`build_preview.py` copies it and injects a `<script>` that installs a fake `window.physalia`, pushes
a full `UiState`, and opens the image editor on a synthetic capture.

```
python tools/uitest/build_preview.py out.html          # stub only
python tools/uitest/test_all_tools.py file:///…/out.html shot.png
python tools/uitest/test_text_canvas.py file:///…/out.html shot.png
```

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
