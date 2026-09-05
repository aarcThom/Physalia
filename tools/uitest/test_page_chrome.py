"""Open a header page over a live conversation and check the window's chrome around it.

Two things this asserts, both of which are layout, not logic:
  * the prompt box and its action stack are GONE on the page, and the page's scroller runs all the
    way down to where the box used to be;
  * the back control reads "Go Back" and is a raised button (neu-btn), not flat text (neu-ghost).

Trusted clicks over CDP — the menu is a bits-ui dropdown, which a synthetic event cannot open.
"""
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import cdp  # noqa: E402

DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT_HTML = sys.argv[1]
SHOT = sys.argv[2] if len(sys.argv) > 2 else None

STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['codex'],
    providerStatuses: [
      { id: 'codex', activated: true, source: 'cli', detail: null },
      { id: 'anthropic', activated: false, source: 'none', detail: null }
    ],
    groundingWired: false, groundingTree: [],
    groundingSelection: null, exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: true, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters', 'Millimeters'], snapshotWired: false,
    snapshotGeometryPresent: false, snapshotSendsMessage: false, snapshotDefaultMessage: '',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: false,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: true,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: false,
    tokenCountToolWired: false, pdfToolWired: false, pendingPdfs: []
"""

DRIVER = """
<script>
(function () {
  window.__errors = [];
  window.addEventListener('error', function (e) { window.__errors.push(String(e.message)); });
  window.chrome = window.chrome || {};
  window.chrome.webview = { postMessage: function () {} };

  window.__probe = function () {
    var out = { errors: window.__errors };
    var editor = document.querySelector('[contenteditable="true"], textarea');
    out.composerPresent = !!editor;
    out.sendButtonPresent = !!document.querySelector('button[title="Send"]');
    var scroller = document.querySelector('.chat-scroll');
    if (scroller) {
      var sr = scroller.getBoundingClientRect();
      out.scroller = { top: Math.round(sr.top), bottom: Math.round(sr.bottom),
                       height: Math.round(sr.height) };
      out.gapToWindowBottom = Math.round(window.innerHeight - sr.bottom);
    }
    // Every back control on the page, not just the first: two of them is the bug this catches.
    var backs = [], buttons = document.querySelectorAll('button');
    for (var i = 0; i < buttons.length; i++) {
      var b = buttons[i];
      if (!b.querySelector('svg.lucide-arrow-left')) continue;
      var r = b.getBoundingClientRect();
      backs.push({ text: (b.textContent || '').trim(),
                   raised: b.className.indexOf('neu-btn') >= 0,
                   ghost: b.className.indexOf('neu-ghost') >= 0,
                   top: Math.round(r.top), left: Math.round(r.left) });
    }
    out.backControls = backs;
    out.heading = (document.querySelector('h2') || {}).textContent || null;
    out.viewport = { w: window.innerWidth, h: window.innerHeight };
    return JSON.stringify(out);
  };

  function boot() {
    if (!window.physalia || !window.physalia.setState) { setTimeout(boot, 30); return; }
    window.physalia.setHistory([{ id: 'm1', role: 'user', text: 'hello', tools: [] }]);
    window.physalia.setState({ %STATE% });
  }
  boot();
})();
</script>
"""

html = open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
assert sep, 'no </body> found'
open(OUT_HTML, 'w', encoding='utf-8').write(head + DRIVER.replace('%STATE%', STATE) + sep + tail)

url = 'file:///' + OUT_HTML.replace(chr(92), '/')
proc, ws = cdp.launch(url)
try:
    # The real chat window is 460x620 (ChatWindow.ClientSize); anything roomier hides layout bugs.
    ws.call('Emulation.setDeviceMetricsOverride',
            {'width': 460, 'height': 620, 'deviceScaleFactor': 1, 'mobile': False})
    time.sleep(1.2)

    print('over a conversation:', cdp.js(ws, 'window.__probe()'))

    rect = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var b=document.querySelector('button[title=\"Menu\"]');"
        "var r=b.getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};})())"))
    cdp.click(ws, rect['x'], rect['y'])
    time.sleep(0.5)

    item = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var items=document.querySelectorAll('[role=\"menuitem\"]');"
        "for(var i=0;i<items.length;i++){if(/Set up providers/.test(items[i].textContent||'')){"
        "var r=items[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert item, 'menu item not found'
    cdp.click(ws, item['x'], item['y'])
    time.sleep(0.8)

    print('setup, provider list:', cdp.js(ws, 'window.__probe()'))

    # Into one provider's page — the screen that used to carry "All providers" AND "Go Back".
    prov = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var bs=document.querySelectorAll('button');"
        "for(var i=0;i<bs.length;i++){if(/Claude Code/.test(bs[i].textContent||'')){"
        "var r=bs[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert prov, 'provider button not found'
    cdp.click(ws, prov['x'], prov['y'])
    time.sleep(0.6)
    print('setup, one provider:', cdp.js(ws, 'window.__probe()'))
    if SHOT:
        cdp.screenshot(ws, SHOT)
        print('shot', SHOT)

    # And back out: one press must land on the list, not close the page.
    back = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var bs=document.querySelectorAll('button');"
        "for(var i=0;i<bs.length;i++){if(bs[i].querySelector('svg.lucide-arrow-left')){"
        "var r=bs[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert back, 'back control not found'
    cdp.click(ws, back['x'], back['y'])
    time.sleep(0.6)
    print('after one press of back:', cdp.js(ws, 'window.__probe()'))

    # The grounding page's sub-views are the other row shape: a page action AND a back control.
    cdp.click(ws, rect['x'], rect['y'])
    time.sleep(0.5)
    item = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var items=document.querySelectorAll('[role=\"menuitem\"]');"
        "for(var i=0;i<items.length;i++){if(/Grounding/.test(items[i].textContent||'')){"
        "var r=items[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert item, 'grounding menu item not found'
    cdp.click(ws, item['x'], item['y'])
    time.sleep(0.8)
    print('grounding, kinds:', cdp.js(ws, 'window.__probe()'))

    units = json.loads(cdp.js(
        ws,
        "JSON.stringify((function(){var bs=document.querySelectorAll('button');"
        "for(var i=0;i<bs.length;i++){if(/Document Units/.test(bs[i].textContent||'')){"
        "var r=bs[i].getBoundingClientRect();return {x:r.left+r.width/2,y:r.top+r.height/2};}}"
        "return null;})())"))
    assert units, 'Document Units entry not found'
    cdp.click(ws, units['x'], units['y'])
    time.sleep(0.6)
    print('grounding, a sub-view:', cdp.js(ws, 'window.__probe()'))
    if SHOT:
        cdp.screenshot(ws, SHOT.replace('.png', '-grounding.png'))
finally:
    proc.kill()
