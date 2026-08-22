"""Verify the input-free text tool with trusted CDP input, by reading the CANVAS PIXELS.

The point of the rewrite is that a note is drawn by the same path as a pen stroke, so the assertion
should be about pixels on the canvas, not about a DOM element existing.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cdp  # noqa: E402

URL = sys.argv[1]
SHOT = sys.argv[2] if len(sys.argv) > 2 else None

# Count red-ish pixels (the default mark-up colour #c0342b) inside a box of IMAGE coordinates.
RED_COUNT = """(function (x, y, w, h) {
  var c = document.querySelector('div.fixed.inset-0 canvas');
  var d = c.getContext('2d').getImageData(x, y, w, h).data;
  var n = 0;
  for (var i = 0; i < d.length; i += 4) {
    if (d[i] > 120 && d[i + 1] < 90 && d[i + 2] < 90) { n++; }
  }
  return n;
})(%d, %d, %d, %d)"""

HINT = "document.querySelector('div.fixed.inset-0 p').textContent.trim()"

proc, ws = cdp.launch(URL)
try:
    ws.call('Page.enable')
    ws.call('Runtime.enable')
    time.sleep(2.5)

    box = cdp.js(ws, """(function () {
      var c = document.querySelector('div.fixed.inset-0 canvas');
      var r = c.getBoundingClientRect();
      return { x: r.left, y: r.top, w: r.width, h: r.height, nw: c.width, nh: c.height };
    })()""")
    print('canvas:', box)

    cdp.js(ws, "document.querySelector('button[aria-label^=\"Text note\"]').click()")
    time.sleep(0.2)
    print('hint before click:', repr(cdp.js(ws, HINT)))

    # Click at 30%/35% of the picture; the same fraction of the natural size is where to look.
    fx, fy = 0.30, 0.35
    ix, iy = int(box['nw'] * fx), int(box['nh'] * fy)
    region = (max(0, ix - 20), max(0, iy - 60), 600, 100)
    print('red before  :', cdp.js(ws, RED_COUNT % region))

    cdp.click(ws, box['x'] + box['w'] * fx, box['y'] + box['h'] * fy)
    time.sleep(0.35)
    print('hint after click:', repr(cdp.js(ws, HINT)))
    caret = cdp.js(ws, RED_COUNT % region)
    print('red after click (caret):', caret)

    cdp.type_keys = None  # not used here; real key events below
    for ch in 'ridge line is wrong':
        ws.call('Input.dispatchKeyEvent', {'type': 'keyDown', 'text': ch, 'unmodifiedText': ch, 'key': ch})
        ws.call('Input.dispatchKeyEvent', {'type': 'keyUp', 'key': ch})
        time.sleep(0.01)
    time.sleep(0.3)
    typed = cdp.js(ws, RED_COUNT % region)
    print('red after typing:', typed)

    # Backspace must take a character back off.
    for _ in range(6):
        cdp.key(ws, 'Backspace', 'Backspace', 8)
        time.sleep(0.02)
    time.sleep(0.3)
    shortened = cdp.js(ws, RED_COUNT % region)
    print('red after 6x backspace:', shortened)

    cdp.key(ws, 'Enter', 'Enter', 13)
    time.sleep(0.35)
    after = cdp.js(ws, """(function () {
      var undo = document.querySelector('button[aria-label="Undo"]');
      return { undoEnabled: !!undo && !undo.disabled };
    })()""")
    print('after Enter:', after, 'hint:', repr(cdp.js(ws, HINT)))
    print('red after Enter (caret gone, text stays):', cdp.js(ws, RED_COUNT % region))

    print()
    print('PASS' if (caret > 0 and typed > caret and shortened < typed and after['undoEnabled'])
          else 'FAIL')

    if SHOT:
        cdp.screenshot(ws, SHOT)
        print('screenshot:', SHOT)
finally:
    proc.kill()
