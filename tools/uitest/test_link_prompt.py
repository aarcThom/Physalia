"""Drive the link-safety prompt: an assistant turn carrying a MASKED markdown link, clicked.

The bug this covers rendered the prompt with none of its styling — no backdrop, no card, no
positioning — so the dialog's text lay straight over the conversation. The assertions are therefore
GEOMETRIC and about paint: the overlay covers the window, the card is opaque, and the conversation
behind it is not what a pixel in the card's middle shows.
"""
import io
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'linkprompt.html')
SHOT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, 'linkprompt.png')

URL = 'https://webtransfer.vancouver.ca/opendata/2013LiDAR/COV_4860E_54570N.zip'
TEXT = ('The likely covering tile is **4860E_54570N**: [download zipped LAS](' + URL + ').\n\n'
        'It covers approximately lon -123.1924 to -123.1787 and lat 49.2656 to 49.2746.')

STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['anthropic'], groundingWired: false, groundingTree: [],
    groundingSelection: null, exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: false, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters'], snapshotWired: false,
    snapshotGeometryPresent: false, snapshotSendsMessage: true, snapshotDefaultMessage: '',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: true,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: false,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: false,
    tokenCountToolWired: false, pdfToolWired: false, pendingPdfs: []
"""

SCRIPT = """
<script>
window.__errs = [];
window.addEventListener('error', function (e) { window.__errs.push(String(e.message)); });
window.chrome = { webview: { postMessage: function () {} } };
function rect(el) {
  if (!el) { return null; }
  var r = el.getBoundingClientRect();
  return { x: Math.round(r.left), y: Math.round(r.top), w: Math.round(r.width), h: Math.round(r.height) };
}
setTimeout(function () {
  window.physalia.setState({%STATE%});
  window.physalia.setHistory([{ id: 'm1', role: 'assistant', text: %TEXT% }]);
  setTimeout(function () {
    // The intercepted link renders as a BUTTON (streamdown swaps the anchor out when it asks first).
    var link = document.querySelector('[data-streamdown="link"]');
    var diag = { linkFound: !!link, linkText: link ? link.textContent.trim() : '' };
    if (link) { link.click(); }
    setTimeout(function () {
      var dialog = document.querySelector('[role="dialog"]');
      var overlay = dialog ? dialog.parentElement : null;
      diag.dialogFound = !!dialog;
      if (dialog) {
        var cs = getComputedStyle(dialog);
        var os = getComputedStyle(overlay);
        diag.overlay = rect(overlay);
        diag.dialog = rect(dialog);
        diag.overlayPosition = os.position;
        diag.overlayBackground = os.backgroundColor;
        diag.cardBackground = cs.backgroundColor;
        diag.cardShadow = cs.boxShadow.slice(0, 40);
        diag.zIndex = os.zIndex;
        diag.window = { w: window.innerWidth, h: window.innerHeight };
        // What sits under the middle of the card: the card itself, never the conversation behind it.
        var mid = document.elementFromPoint(diag.dialog.x + diag.dialog.w / 2,
                                            diag.dialog.y + diag.dialog.h / 2);
        diag.topAtCardCentre = mid ? (mid.getAttribute('data-streamdown') || mid.tagName) : null;
        diag.cardContainsTop = mid ? dialog.contains(mid) : false;
        diag.urlShown = dialog.textContent.indexOf('COV_4860E_54570N.zip') >= 0;
        diag.buttons = Array.prototype.map.call(dialog.querySelectorAll('button'),
          function (b) { return b.textContent.trim(); });
      }
      diag.errs = window.__errs.slice(0, 4);
      document.documentElement.setAttribute('data-diag', JSON.stringify(diag));
    }, 300);
  }, 300);
}, 400);
</script>
"""

script = SCRIPT.replace('%STATE%', STATE).replace('%TEXT%', json.dumps(TEXT))
html = io.open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
io.open(OUT, 'w', encoding='utf-8').write(head + script + sep + tail)

url = 'file:///' + OUT.replace(chr(92), '/')
common = [CHROME, '--headless=new', '--disable-gpu', '--window-size=460,620',
          '--virtual-time-budget=9000', url]
dom = subprocess.run(common + ['--dump-dom'], capture_output=True, text=True, encoding='utf-8').stdout
subprocess.run(common + ['--screenshot=' + SHOT], capture_output=True)

start = dom.find('data-diag="')
if start < 0:
    print('no data-diag; page did not run')
    sys.exit(1)
raw = dom[start + len('data-diag="'):]
raw = raw[:raw.find('"')]
raw = raw.replace('&quot;', '"').replace('&amp;', '&').replace('&lt;', '<').replace('&gt;', '>')
print(json.dumps(json.loads(raw), indent=2))
print('screenshot:', SHOT)
