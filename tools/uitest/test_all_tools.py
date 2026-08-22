"""Every tool, driven with trusted CDP input, ending in a confirm — the whole editor in one pass."""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cdp  # noqa: E402

URL = sys.argv[1]
SHOT = sys.argv[2] if len(sys.argv) > 2 else None

MARKS = """(function () {
  var undo = document.querySelector('button[aria-label="Undo"]');
  var redo = document.querySelector('button[aria-label="Redo"]');
  return { undo: !!undo && !undo.disabled, redo: !!redo && !redo.disabled };
})()"""


def red(ws, x, y, w, h):
    return cdp.js(ws, """(function (x, y, w, h) {
      var c = document.querySelector('div.fixed.inset-0 canvas');
      var d = c.getContext('2d').getImageData(x, y, w, h).data;
      var n = 0;
      for (var i = 0; i < d.length; i += 4) {
        if (d[i] > 120 && d[i + 1] < 90 && d[i + 2] < 90) { n++; }
      }
      return n;
    })(%d, %d, %d, %d)""" % (x, y, w, h))


def tool(ws, name):
    cdp.js(ws, "document.querySelector('button[aria-label^=\"%s\"]').click()" % name)
    time.sleep(0.15)


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

    def vx(f):
        return box['x'] + box['w'] * f

    def vy(f):
        return box['y'] + box['h'] * f

    # Pen: a squiggle across the top of the picture.
    tool(ws, 'Freehand')
    cdp.drag(ws, [(vx(0.08 + i * 0.02), vy(0.12 + (0.03 if i % 2 else -0.03))) for i in range(20)])
    time.sleep(0.3)
    pen = red(ws, 0, 0, box['nw'], int(box['nh'] * 0.3))
    print('pen stroke red px:', pen, MARKS and cdp.js(ws, MARKS))

    # Arrow: two clicks.
    tool(ws, 'Arrow')
    cdp.click(ws, vx(0.15), vy(0.75))
    time.sleep(0.15)
    cdp.click(ws, vx(0.40), vy(0.52))
    time.sleep(0.3)
    arrow = red(ws, 0, int(box['nh'] * 0.45), box['nw'], int(box['nh'] * 0.45))
    print('arrow red px:', arrow, cdp.js(ws, MARKS))

    # Note.
    tool(ws, 'Text note')
    cdp.click(ws, vx(0.45), vy(0.62))
    for ch in 'span is short':
        ws.call('Input.dispatchKeyEvent', {'type': 'keyDown', 'text': ch, 'unmodifiedText': ch, 'key': ch})
        ws.call('Input.dispatchKeyEvent', {'type': 'keyUp', 'key': ch})
    time.sleep(0.25)
    cdp.key(ws, 'Enter', 'Enter', 13)
    time.sleep(0.3)
    print('after note:', cdp.js(ws, MARKS))

    # Eraser over the squiggle only: the arrow and the note must survive.
    tool(ws, 'Erase marks')
    cdp.drag(ws, [(vx(0.10 + i * 0.01), vy(0.12)) for i in range(10)])
    time.sleep(0.3)
    penAfter = red(ws, 0, 0, box['nw'], int(box['nh'] * 0.3))
    arrowAfter = red(ws, 0, int(box['nh'] * 0.45), box['nw'], int(box['nh'] * 0.45))
    print('after erase — pen red px:', penAfter, 'arrow+note red px:', arrowAfter)

    # Undo brings the squiggle back; redo takes it away again.
    cdp.js(ws, "document.querySelector('button[aria-label=\"Undo\"]').click()")
    time.sleep(0.3)
    undone = red(ws, 0, 0, box['nw'], int(box['nh'] * 0.3))
    cdp.js(ws, "document.querySelector('button[aria-label=\"Redo\"]').click()")
    time.sleep(0.3)
    redone = red(ws, 0, 0, box['nw'], int(box['nh'] * 0.3))
    print('undo restores pen:', undone, ' redo removes it again:', redone)

    if SHOT:
        # Put the squiggle back for the picture.
        cdp.js(ws, "document.querySelector('button[aria-label=\"Undo\"]').click()")
        time.sleep(0.3)
        cdp.screenshot(ws, SHOT)
        print('screenshot:', SHOT)

    cdp.js(ws, "document.querySelector('button[aria-label=\"Confirm\"]').click()")
    time.sleep(0.5)
    sent = cdp.js(ws, """(function () {
      if (!window.__sent) { return null; }
      var m = JSON.parse(window.__sent);
      return { kind: m.kind, images: m.images.length, text: m.text, b64: m.images[0].base64.length };
    })()""")
    closed = cdp.js(ws, "!document.querySelector('div.fixed.inset-0 canvas')")
    print('confirm sent:', sent, 'editor closed:', closed)

    ok = (pen > 100 and arrow > 100 and penAfter < pen * 0.2 and arrowAfter > 100
          and undone > 100 and redone < undone * 0.2 and sent and sent['kind'] == 'geometry-snapshot' and closed)
    print()
    print('PASS' if ok else 'FAIL')
finally:
    proc.kill()
