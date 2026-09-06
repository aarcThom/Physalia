"""Render one answer carrying every markdown shape the chat renders as PROSE, and measure how it is laid out.

Written for the Tailwind `@source` change: streamdown's theme classes live in node_modules, which
Tailwind does not scan, so before that line the whole theme was dead and markdown fell back to
browser defaults. The assertions are the ones that distinguish those two states — list indent,
table borders and cell padding, heading scale, code-block background — plus a screenshot, since the
rest of the judgement is visual.

The sample deliberately uses a ```python fence, not ```json: a JSON block is collapsed into its own
JsonBlock, and AssistantTurnGroup folds everything BEFORE the first JSON block into the collapsed
Thinking section — which is where the whole sample went on the first run, reading as a blank answer.
"""
import io
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
CHROME = 'C:/Program Files/Google/Chrome/Application/chrome.exe'

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'markdown.html')
SHOT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(HERE, 'markdown.png')

TEXT = """## Contour tiles

The likely covering tile is **4860E_54570N**: [download zipped LAS](https://webtransfer.vancouver.ca/opendata/2013LiDAR/COV_4860E_54570N.zip).

### What to wire

1. A `Deconstruct Brep` on the massing solid.
2. Cull the faces whose normal points *down*.
3. Feed the rest to the transmitter.

- Curves only on that output — it is locked.
- Points go on a second output.

| field | type | example |
| --- | --- | --- |
| tile | text | 4860E_54570N |
| points | integer | 1284004 |
| year | integer | 2013 |

> The lock freezes NAMES, not hints — a wrong hint can still be corrected.

```python
records = api.get("/api/records", limit=100)
print(len(records))
```

Use `max_records` to walk more than one page.

---

That is the whole set.
"""

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
// Scope every query to the rendered answer: the page holds other h2/table markup (setup pages, the
// link prompt's own template) and document.querySelector would sample whichever came first.
var root = null;
function box(sel, props) {
  var el = (root || document).querySelector(sel);
  if (!el) { return null; }
  var cs = getComputedStyle(el);
  var out = { w: Math.round(el.getBoundingClientRect().width) };
  props.forEach(function (p) { out[p] = cs[p]; });
  return out;
}
setTimeout(function () {
  window.physalia.setState({%STATE%});
  window.physalia.setHistory([{ id: 'm1', role: 'assistant', text: %TEXT% }]);
  setTimeout(function () {
    var heading = Array.prototype.filter.call(document.querySelectorAll('h2'), function (h) {
      return h.textContent.indexOf('Contour tiles') >= 0;
    })[0];
    // Every markdown block is its own container, so scope to the whole answer, not the heading's block.
    root = heading ? (heading.closest('[data-slot="message-content"]') || heading.parentElement) : null;
    var scroller = document.querySelector('[data-slot="conversation"], .overflow-y-auto');
    document.documentElement.setAttribute('data-diag', JSON.stringify({
      rootFound: !!root,
      h2: box('h2', ['fontSize', 'fontWeight', 'marginTop', 'marginBottom']),
      h3: box('h3', ['fontSize', 'marginTop']),
      ol: box('ol', ['listStyleType', 'paddingInlineStart', 'marginTop']),
      ul: box('ul', ['listStyleType', 'paddingInlineStart']),
      table: box('table', ['borderCollapse']),
      td: box('td', ['padding', 'borderBottomWidth', 'borderBottomStyle']),
      th: box('th', ['padding', 'fontWeight', 'textAlign']),
      pre: box('pre', ['backgroundColor', 'padding', 'borderRadius', 'overflowX']),
      codeInline: box('p code', ['backgroundColor', 'fontSize', 'padding']),
      blockquote: box('blockquote', ['borderLeftWidth', 'paddingLeft', 'fontStyle']),
      hr: box('hr', ['borderTopWidth', 'marginTop']),
      link: box('[data-streamdown="link"]', ['color', 'textDecorationLine', 'fontWeight']),
      contentWidth: scroller ? Math.round(scroller.getBoundingClientRect().width) : -1,
      overflowsX: document.documentElement.scrollWidth > document.documentElement.clientWidth,
      errs: window.__errs.slice(0, 4)
    }));
  }, 700);
}, 400);
</script>
"""

script = SCRIPT.replace('%STATE%', STATE).replace('%TEXT%', json.dumps(TEXT))
html = io.open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
io.open(OUT, 'w', encoding='utf-8').write(head + script + sep + tail)

url = 'file:///' + os.path.abspath(OUT).replace(chr(92), '/')
# Measurements run at the real client size (460x620 — see the README); the screenshot is taken on a
# tall window as well, since the whole point of the visual check is to see every block at once.
common = [CHROME, '--headless=new', '--disable-gpu', '--window-size=460,620',
          '--virtual-time-budget=12000', url]
tall = [CHROME, '--headless=new', '--disable-gpu', '--window-size=460,1500',
        '--virtual-time-budget=12000', url]
dom = ''
for _ in range(3):  # a cold profile occasionally returns before the page's timer chain has run
    dom = subprocess.run(common + ['--dump-dom'], capture_output=True, text=True,
                         encoding='utf-8', errors='replace').stdout or ''
    if 'data-diag="' in dom:
        break
subprocess.run(common + ['--screenshot=' + os.path.abspath(SHOT)], capture_output=True)
TALL_SHOT = os.path.splitext(os.path.abspath(SHOT))[0] + '-tall.png'
subprocess.run(tall + ['--screenshot=' + TALL_SHOT], capture_output=True)

start = dom.find('data-diag="')
if start < 0:
    print('no data-diag; page did not run')
    sys.exit(1)
raw = dom[start + len('data-diag="'):]
raw = raw[:raw.find('"')]
raw = raw.replace('&quot;', '"').replace('&amp;', '&').replace('&lt;', '<').replace('&gt;', '>')
print(json.dumps(json.loads(raw), indent=2))
print('screenshot:', SHOT)
print('screenshot:', TALL_SHOT)
