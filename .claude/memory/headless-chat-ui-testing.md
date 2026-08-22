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

Recipe (working script: scratchpad `build_preview.py` pattern):
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

Used to verify [[image-mark-up-tool]]. Complements [[core-console-harness]] (same idea for Core).
