---
name: headless-chat-ui-testing
description: How to exercise the Physalia chat UI without Rhino — drive the built bundle in headless Chrome and read results back through a DOM attribute.
metadata: 
  node_type: memory
  type: reference
  originSessionId: 00c332bc-4327-401f-8a58-85ce366d3e46
  modified: 2026-08-22T06:27:21.046Z
---

The chat UI (`src/Physalia.UI`) can be tested **without Rhino or the Eto WebView**: the production
bundle is one self-contained `dist/index.html`, so stub `window.physalia` yourself and drive it.
No puppeteer/playwright on this box — Chrome's own flags are enough
(`C:\Program Files\Google\Chrome\Application\chrome.exe`).

Recipe (working scripts: `tools/uitest/` — `build_preview.py` builds the stubbed page,
`test_text_canvas.py` and `test_all_tools.py` drive it):
1. Copy `dist/index.html`, and insert a `<script>` **before the LAST `</body>`**.
2. In it: install an error collector, stub `window.chrome.webview.postMessage` to capture outgoing
   payloads instead of navigating, then call `window.physalia.setState({...})` / `setHistory([])` —
   a full `UiState` literal, since `setState` assigns some fields without a `??` default.
3. Drive the UI with real DOM calls: `button[aria-label="…"].click()`, and `dispatchEvent(new
   PointerEvent(...))` with `pointerId: 1` for canvas work.
4. Report results by writing JSON into `document.documentElement.setAttribute('data-diag', …)`, then
   read it with `chrome --headless=new --virtual-time-budget=12000 --dump-dom | grep -o 'data-diag="[^"]*"'`.
   `--screenshot=out.png` with `--window-size` gives the visual check.
   `--virtual-time-budget` is what lets the page's `setTimeout` chain run before capture.

**Two traps, both cost a wrong diagnosis:**
- **`</body>` occurs inside the inlined app JS too.** A `str.replace('</body>', …)` (which replaces
  ALL occurrences in Python) injects the script into JS string literals and corrupts the bundle —
  the symptom is `Uncaught SyntaxError: Unexpected end of input` and an undefined `window.physalia`.
  Use `rpartition('</body>')`.
- **Do not measure the DOM in the same tick as the click that changes it.** Svelte has not flushed;
  an overlay that did close still reads as present. Put the assertion in a later `setTimeout`.

Also: `.fixed.inset-0.z-50` is not a unique selector — streamdown ships a link-safety modal with the
same classes. Select the editor by "the fixed overlay containing a canvas".

**Synthetic events are not enough, and trusting them cost a false pass.** `dispatchEvent(new
PointerEvent(...))` runs no DEFAULT ACTION, so it can neither reproduce nor disprove anything about
focus, selection or the compatibility mouse events — the image editor's text tool passed that harness
and was broken in Rhino. For anything input-shaped, inject **trusted** events over CDP instead:
`tools/uitest/cdp.py` (in the repo, with a README and the two driver scripts) is a ~120-line client
(hand-rolled WebSocket frames — no websocket library on this box) exposing `launch()`, `js()`, `click()`, `drag()`, `type_keys()`, `key()`, `screenshot()` against
`chrome --headless=new --remote-debugging-port=9333`, reading the target from
`http://127.0.0.1:9333/json`. `Input.dispatchKeyEvent` needs `text` on the keyDown to insert a
character; `Input.insertText` bypasses the keyboard entirely and hides key-handler bugs.

**Assert on pixels, not on elements, whenever the feature draws.** `getImageData` on the live canvas
and count the mark-up colour: a caret appearing, a count growing as you type and shrinking on
backspace, an eraser drag zeroing one region while another survives. That harness is indifferent to
HOW the feature is built, so it survived the text tool being rewritten from an `<input>` to canvas
drawing — a DOM-shaped assertion would have had to be rewritten with it.

**Run the control.** Build the PRE-fix bundle and drive it too. That is what showed the first fix was
treating the wrong cause: the "broken" build passed in Chrome, which is how the failure was pinned as
WebView-only rather than logic.

Used to verify [[image-mark-up-tool]]. Complements [[core-console-harness]] (same idea for Core).

**2026-09-05: it measures LAYOUT too, and that is what found the "setup page only appears when I
scroll" bug.** `tools/uitest/test_setup_visible.py` puts the bundle in first-run state
(`needsSetup`, empty `configuredProviders`/`providerStatuses`) and reports the scroller's
`scrollTop`/`scrollHeight`/`clientHeight` plus the welcome block's `getBoundingClientRect()`.

- **Drive it at the REAL window size.** `ChatWindow.ClientSize` is **460x620**; at the 520x900 I
  tried first everything fitted and the page looked innocent. At 460x620 the 120px `HappyFace` plus
  its gaps put the welcome block at y=236 inside a 321px scroller, with every provider button below
  the fold — which reads as an empty screen. After the fix it sits at y=84.
- **`inViewport` computed against `window.innerHeight` is a LIE** when the content lives in an inner
  scroller. Compare the element's rect against the SCROLLER's rect, not the window's.
- Running it also ruled the page *logic* out: the surface rendered correctly at `scrollTop: 0` in
  Chrome, so the remaining suspect was host-side state timing (`needsSetup` staying false until an
  async probe returned) — a second, real bug that reading alone had not pinned.

Same lesson as the pixel-assertion note above: measure the geometry, not the mere presence of the
element. "The node is in the DOM" was true the whole time it was invisible.
