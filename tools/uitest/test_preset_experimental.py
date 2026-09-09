"""Drive the preset gallery's Experimental section — the AI folder folded behind one pink button.

Three things this asserts that a DOM check alone would not:

  * the model-written presets are NOT in the gallery while the section is shut. "The rows exist in
    the DOM" is exactly the failure to avoid: the whole point of the section is that these are
    opt-in, so the assertion is on rendered row COUNT and on the AI names being absent.
  * the button is actually PINK, read back as a computed colour and converted to a hue, not as a
    class name. A class that Tailwind never compiled (an arbitrary value it did not see, a token
    defined only in a dark block) leaves the markup looking right and the button grey.
  * the warning reads verbatim, island emoji included, AFTER the round trip through the bundle —
    the text is authored as \\u escapes, and an encoding fault on the way into dist/index.html
    would show up here rather than in Rhino.

Trusted clicks over CDP: the gallery is reached through a bits-ui dropdown, which a synthetic
event cannot open.
"""
import json
import os
import sys
import time

# The warning carries an emoji, and this console is cp1252 by default.
sys.stdout.reconfigure(encoding='utf-8', errors='replace')

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cdp  # noqa: E402

DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT_HTML = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, 'preset_experimental.html')
SHOT = sys.argv[2] if len(sys.argv) > 2 else None

# The warning as the user must read it. Kept here as a literal, so this file and Preset.svelte
# disagreeing is a failure rather than a shared typo.
WARNING = (
    'WELCOME TO THE SLOP ZONE! All harnesses here are AI generated. '
    "I'll be working through human written replacements after I get back from vacation "
    '\U0001f3dd️. They are worth going through if a specific topic interests you. '
    'I asked Claude to demonstrate ALL components. Happy tinkering!'
)

STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['anthropic'],
    providerStatuses: [{ id: 'anthropic', activated: true, source: 'store', detail: null }],
    groundingWired: false, groundingTree: [], groundingSelection: null,
    exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: false, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters'], snapshotWired: false,
    snapshotGeometryPresent: false, snapshotSendsMessage: false, snapshotDefaultMessage: '',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: false,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: false,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: false,
    tokenCountToolWired: false, pdfToolWired: false, pendingPdfs: []
"""

# What the host pushes: the shipped folder, the user's own, and the AI folder last — the order
# PresetLibrary.Enumerate produces.
PRESETS = """[
  { file: 'Physalia/Claude Code - Python 3.phy', folder: 'Physalia',
    name: 'Claude Code - Python 3', description: 'Writes Python for you.' },
  { file: 'User/My Harness.phy', folder: 'User', name: 'My Harness', description: null },
  { file: 'AI/01 - Talk to a Model.phy', folder: 'AI', name: '01 - Talk to a Model',
    description: 'The smallest pipeline there is.' },
  { file: 'AI/02 - What the Model Knows.phy', folder: 'AI', name: '02 - What the Model Knows',
    description: 'Grounding, one input at a time.' },
  { file: 'AI/28 - Can This Actually Be Made.phy', folder: 'AI',
    name: '28 - Can This Actually Be Made', description: 'Fabrication review.' }
]"""

DRIVER = """
<script>
(function () {
  window.__errors = [];
  window.addEventListener('error', function (e) { window.__errors.push(String(e.message)); });
  window.chrome = window.chrome || {};
  window.chrome.webview = { postMessage: function () {} };

  // The row buttons are the ones carrying a plus icon; the section toggle carries the flask.
  function rows() {
    var out = [], bs = document.querySelectorAll('button');
    for (var i = 0; i < bs.length; i++) {
      if (bs[i].querySelector('svg.lucide-plus')) {
        out.push((bs[i].textContent || '').trim());
      }
    }
    return out;
  }

  function toggle() {
    var bs = document.querySelectorAll('button');
    for (var i = 0; i < bs.length; i++) {
      if (bs[i].querySelector('svg.lucide-flask-conical')) { return bs[i]; }
    }
    return null;
  }

  // A computed colour reduced to hue/saturation/lightness, so "is it pink" is answered by the
  // colour that actually gets painted rather than by a class name that may never have compiled.
  //
  // The tokens are authored in oklch and Tailwind emits a lab() fallback, so getComputedStyle
  // hands back a CSS Color 4 value and not rgb() — the reduction therefore goes through a canvas,
  // which resolves whatever the browser can paint into sRGB bytes. A value the canvas cannot
  // parse leaves fillStyle at the marker set just before it, so an unreadable colour reports
  // itself instead of arriving as a plausible number.
  var probeCanvas = document.createElement('canvas');
  probeCanvas.width = 1; probeCanvas.height = 1;
  function hsl(value) {
    if (!value) { return null; }
    var ctx = probeCanvas.getContext('2d');
    ctx.fillStyle = '#00ff00';
    ctx.fillStyle = value;
    if (ctx.fillStyle === '#00ff00') { return { unparsed: value }; }
    ctx.fillRect(0, 0, 1, 1);
    var px = ctx.getImageData(0, 0, 1, 1).data;
    var p = [px[0], px[1], px[2]];
    var r = p[0] / 255, g = p[1] / 255, b = p[2] / 255;
    var max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
    var h = 0;
    if (d !== 0) {
      if (max === r) { h = ((g - b) / d) % 6; }
      else if (max === g) { h = (b - r) / d + 2; }
      else { h = (r - g) / d + 4; }
      h *= 60;
      if (h < 0) { h += 360; }
    }
    var l = (max + min) / 2;
    var s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
    return { h: Math.round(h), s: Math.round(s * 100), l: Math.round(l * 100),
             rgb: p, css: value };
  }

  window.__probe = function () {
    var out = { errors: window.__errors, rows: rows() };
    var t = toggle();
    out.togglePresent = !!t;
    if (t) {
      var cs = getComputedStyle(t);
      out.toggleText = (t.textContent || '').trim();
      out.expanded = t.getAttribute('aria-expanded');
      out.background = hsl(cs.backgroundColor);
      out.colour = hsl(cs.color);
      var r = t.getBoundingClientRect();
      out.toggleRect = { top: Math.round(r.top), height: Math.round(r.height) };
      // What the user's pointer actually lands on: a row drawn over the toggle would still
      // measure pink here.
      var hit = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
      out.toggleHit = !!(hit && (hit === t || t.contains(hit)));
    }
    // The warning, wherever it is on the page — matched by its opening words, not by position.
    var warn = null;
    document.querySelectorAll('p').forEach(function (p) {
      if (/SLOP ZONE/.test(p.textContent || '')) { warn = p; }
    });
    out.warning = warn ? (warn.textContent || '').trim() : null;
    if (warn) {
      var wr = warn.getBoundingClientRect(), tr = toggle().getBoundingClientRect();
      out.warningAboveRows = Math.round(wr.top - tr.bottom);
      out.warningColour = hsl(getComputedStyle(warn).color);
    }
    out.headings = Array.prototype.map.call(
      document.querySelectorAll('h3'), function (h) { return (h.textContent || '').trim(); });
    // A page that grew a horizontal scroll is a layout bug the screenshot would not show.
    var sc = document.querySelector('.chat-scroll');
    out.overflowX = sc ? sc.scrollWidth - sc.clientWidth : null;
    return JSON.stringify(out);
  };

  function boot() {
    if (!window.physalia || !window.physalia.setState) { setTimeout(boot, 30); return; }
    window.physalia.setHistory([]);
    window.physalia.setState({ %STATE% });
    window.physalia.setPresets(%PRESETS%);
  }
  boot();
})();
</script>
"""


def probe(ws, label):
    """Reads the page and prints it, returning the parsed result."""
    out = json.loads(cdp.js(ws, 'window.__probe()'))
    print(label + ':', json.dumps(out, ensure_ascii=False))
    return out


def find(ws, pattern):
    """Centre of the first button whose text matches, or None."""
    return json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var bs=document.querySelectorAll('button');"
        "for(var i=0;i<bs.length;i++){if(/" + pattern + "/.test(bs[i].textContent||'')){"
        "var r=bs[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))


html = open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
assert sep, 'no </body> found'
open(OUT_HTML, 'w', encoding='utf-8').write(
    head + DRIVER.replace('%STATE%', STATE).replace('%PRESETS%', PRESETS) + sep + tail)

url = 'file:///' + OUT_HTML.replace(chr(92), '/')
proc, ws = cdp.launch(url)
failures = []
try:
    # ChatWindow's real client size: a roomier viewport hides layout faults.
    ws.call('Emulation.setDeviceMetricsOverride',
            {'width': 460, 'height': 620, 'deviceScaleFactor': 1, 'mobile': False})
    time.sleep(1.2)

    menu = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var b=document.querySelector('button[title=\"Menu\"]');"
        "var r=b.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})())"))
    cdp.click(ws, menu['x'], menu['y'])
    time.sleep(0.5)

    item = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var items=document.querySelectorAll('[role=\"menuitem\"]');"
        "for(var i=0;i<items.length;i++){if(/Add preset/.test(items[i].textContent||'')){"
        "var r=items[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert item, 'Add preset menu item not found'
    cdp.click(ws, item['x'], item['y'])
    time.sleep(0.8)

    shut = probe(ws, 'gallery, section shut')

    if not shut['togglePresent']:
        failures.append('no Experimental button on the page')
    # The label, then a count of what it hides — so the button says how much is behind it.
    if not shut['toggleText'].startswith('Experimental'):
        failures.append('button says %r, not "Experimental"' % shut['toggleText'])
    if not shut['toggleText'].rstrip().endswith('3'):
        failures.append('button does not show the 3 harnesses it holds: %r'
                        % shut['toggleText'])
    if shut['expanded'] != 'false':
        failures.append('shut section reports aria-expanded=%r' % shut['expanded'])
    if not shut['toggleHit']:
        failures.append('something is drawn over the Experimental button')

    # Opt-in means opt-in: none of the AI harnesses may be on the page yet.
    leaked = [r for r in shut['rows'] if 'Talk to a Model' in r or 'Can This Actually' in r]
    if leaked:
        failures.append('AI presets listed while shut: %s' % leaked)
    if shut['warning'] is not None:
        failures.append('warning shown while shut')
    if shut['headings'] != ['Physalia', 'Yours']:
        failures.append('gallery headings are %s' % shut['headings'])
    if 'AI' in shut['headings']:
        failures.append('AI got a heading of its own in the gallery')

    # Pink, measured. Magenta-through-red hues with real saturation; not the page's blue (~205-250)
    # and not a washed-out grey.
    bg = shut['background']
    if bg is None or not (bg['h'] >= 300 or bg['h'] <= 20) or bg['s'] < 25:
        failures.append('button background is not pink: %s' % bg)
    fg = shut['colour']
    if fg is None or not (fg['h'] >= 300 or fg['h'] <= 20) or fg['s'] < 20:
        failures.append('button text is not pink: %s' % fg)

    if SHOT:
        cdp.screenshot(ws, SHOT)
        print('shot', SHOT)

    where = find(ws, 'Experimental')
    assert where, 'Experimental button not found for clicking'
    cdp.click(ws, where['x'], where['y'])
    time.sleep(0.6)

    open_ = probe(ws, 'gallery, section open')

    if open_['expanded'] != 'true':
        failures.append('open section reports aria-expanded=%r' % open_['expanded'])
    if open_['warning'] != WARNING:
        failures.append('warning text differs:\n  got  %r\n  want %r'
                        % (open_['warning'], WARNING))
    for name in ('01 - Talk to a Model', '02 - What the Model Knows',
                 '28 - Can This Actually Be Made'):
        if not any(name in r for r in open_['rows']):
            failures.append('%s missing after opening' % name)
    # The gallery's own presets must survive the expansion.
    if not any('Claude Code - Python 3' in r for r in open_['rows']):
        failures.append('the shipped gallery lost its rows when the section opened')
    if open_['warningAboveRows'] is None or open_['warningAboveRows'] > 24:
        failures.append('warning is not directly under the button: %s'
                        % open_['warningAboveRows'])
    if open_['overflowX']:
        failures.append('the page scrolls sideways by %spx' % open_['overflowX'])
    if open_['errors']:
        failures.append('page errors: %s' % open_['errors'])

    if SHOT:
        cdp.screenshot(ws, SHOT.replace('.png', '-open.png'))
        print('shot', SHOT.replace('.png', '-open.png'))

    # And shut again — a toggle that only opens is half a toggle.
    cdp.click(ws, where['x'], where['y'])
    time.sleep(0.5)
    again = probe(ws, 'gallery, shut again')
    if any('Talk to a Model' in r for r in again['rows']):
        failures.append('the section did not close')
finally:
    proc.kill()

print()
if failures:
    for f in failures:
        print('FAIL', f)
    sys.exit(1)
print('PASS: experimental section is pink, opt-in, and carries the warning verbatim')
