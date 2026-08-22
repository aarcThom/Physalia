"""Build a driveable copy of the chat bundle: stub the host, open the image editor on a synthetic
capture, then optionally drive the mark-up tools with synthetic pointer events.

The script MUST be inserted before the LAST </body> — the inlined app JS contains that string too,
and a global replace corrupts the bundle (found the hard way).
"""
import io
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT = sys.argv[1]
DRIVE = len(sys.argv) > 2 and sys.argv[2] == 'drive'

STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['anthropic'], groundingWired: false, groundingTree: [],
    groundingSelection: null, exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: false, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters'], snapshotWired: true,
    snapshotGeometryPresent: true, snapshotSendsMessage: true, snapshotDefaultMessage: 'msg',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: true,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: true,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: true
"""

DRIVE_JS = """
  // Drive the tools with synthetic pointer events: a freehand squiggle, an arrow (two clicks), a
  // text note, then an eraser stroke over part of the squiggle.
  function pt(target, type, x, y, buttons) {
    var r = target.getBoundingClientRect();
    target.dispatchEvent(new PointerEvent(type, {
      bubbles: true, cancelable: true, pointerId: 1, isPrimary: true, button: 0,
      buttons: buttons === undefined ? 1 : buttons,
      clientX: r.left + x, clientY: r.top + y
    }));
  }
  function tool(name) {
    var b = document.querySelector('button[aria-label^="' + name + '"]');
    if (b) { b.click(); }
    return !!b;
  }
  var cv = document.querySelector('canvas');
  var found = { pen: tool('Freehand') };
  pt(cv, 'pointerdown', 80, 90);
  for (var i = 0; i < 24; i++) { pt(cv, 'pointermove', 80 + i * 8, 90 + Math.sin(i / 2) * 24); }
  pt(cv, 'pointerup', 272, 90, 0);

  found.arrow = tool('Arrow');
  pt(cv, 'pointerdown', 120, 300);
  pt(cv, 'pointerup', 120, 300, 0);
  pt(cv, 'pointermove', 300, 220, 0);
  pt(cv, 'pointerdown', 300, 220);
  pt(cv, 'pointerup', 300, 220, 0);

  found.text = tool('Text note');
  pt(cv, 'pointerdown', 330, 340);
  pt(cv, 'pointerup', 330, 340, 0);
  setTimeout(function () {
    var input = document.querySelector('input[placeholder="note"]');
    found.input = !!input;
    if (input) {
      input.value = 'check this junction';
      input.dispatchEvent(new Event('input', { bubbles: true }));
      input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    }
    found.eraser = tool('Erase marks');
    pt(cv, 'pointerdown', 120, 90);
    for (var j = 0; j < 8; j++) { pt(cv, 'pointermove', 120 + j * 6, 90); }
    pt(cv, 'pointerup', 168, 90, 0);
    found.penAgain = tool('Freehand');
    window.__drive = found;
  }, 200);
"""

script = """
<script>
window.__errs = [];
window.__sent = null;
window.addEventListener('error', function (e) { window.__errs.push(String(e.message)); });
// Stand in for the WebView2 message channel so a confirmed send is captured instead of navigating.
window.chrome = { webview: { postMessage: function (m) { window.__sent = m; } } };
setTimeout(function () {
  window.physalia.setState({%STATE%});
  window.physalia.setHistory([]);
  var c = document.createElement('canvas');
  c.width = 1200; c.height = 780;
  var g = c.getContext('2d');
  var grad = g.createLinearGradient(0, 0, 0, 780);
  grad.addColorStop(0, '#dfe9f2'); grad.addColorStop(1, '#9fb4c4');
  g.fillStyle = grad; g.fillRect(0, 0, 1200, 780);
  g.strokeStyle = '#4a5a68'; g.lineWidth = 4;
  g.strokeRect(300, 320, 400, 300);
  g.beginPath(); g.moveTo(300, 320); g.lineTo(500, 190); g.lineTo(700, 320); g.stroke();
  g.fillStyle = '#33414d'; g.font = '28px sans-serif';
  g.fillText('captured viewport', 420, 700);
  setTimeout(function () {
    window.physalia.markUpSnapshot(
      { base64: c.toDataURL('image/png').split(',')[1], mediaType: 'image/png' },
      'geometry-snapshot');
    setTimeout(function () {
%DRIVE%
      setTimeout(function () {
        var editor = null;
        document.querySelectorAll('div.fixed.inset-0').forEach(function (d) {
          if (d.querySelector('canvas')) { editor = d; }
        });
        document.documentElement.setAttribute('data-diag', JSON.stringify({
          errs: window.__errs.slice(0, 4),
          drive: window.__drive || null,
          editorFound: !!editor,
          buttons: editor ? editor.querySelectorAll('button').length : -1,
          sentLen: window.__sent ? window.__sent.length : 0,
          sentKind: window.__sent ? (JSON.parse(window.__sent).kind || '') : ''
        }));
      }, 400);
    }, 300);
  }, 250);
}, 400);
</script>
"""

script = script.replace('%STATE%', STATE).replace('%DRIVE%', DRIVE_JS if DRIVE else '')
html = io.open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
io.open(OUT, 'w', encoding='utf-8').write(head + script + sep + tail)
print('wrote', OUT, 'drive=', DRIVE)
