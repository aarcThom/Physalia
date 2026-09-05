"""Reproduce the "setup page only appears once you touch the scrollbar" bug.

Builds a stubbed copy of the bundle in first-run state (needsSetup, nothing configured), then
reports where the setup surface actually sits: the scroller's scrollTop/scrollHeight, and the
bounding rect of the setup heading relative to the viewport. If the heading is below the fold on a
freshly mounted page, that is the bug.

Insert the driver before the LAST </body> — the inlined app JS contains that string too.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT = sys.argv[1]

STATE = """
    connected: false, busy: false, needsSetup: true, home: false, status: 'Setup mode',
    configuredProviders: [],
    providerStatuses: [
      { id: 'claude-code', activated: false, source: 'none', detail: null },
      { id: 'anthropic',   activated: false, source: 'none', detail: null }
    ],
    groundingWired: false, groundingTree: [],
    groundingSelection: null, exposeSignatures: false, availableComponents: [],
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

DRIVER = """
<script>
(function () {
  var errors = [];
  window.addEventListener('error', function (e) { errors.push(String(e.message)); });

  window.chrome = window.chrome || {};
  window.chrome.webview = { postMessage: function () {} };

  function report(tag) {
    var out = { tag: tag, errors: errors };
    try {
      var scroller = document.querySelector('.chat-scroll');
      out.scrollerFound = !!scroller;
      if (scroller) {
        var sr = scroller.getBoundingClientRect();
        out.scroller = {
          scrollTop: scroller.scrollTop,
          scrollHeight: scroller.scrollHeight,
          clientHeight: scroller.clientHeight,
          top: Math.round(sr.top), height: Math.round(sr.height)
        };
      }

      // The first-run copy lives in a .neu-raised block; the provider buttons follow it.
      var welcome = null;
      var blocks = document.querySelectorAll('.neu-raised');
      for (var i = 0; i < blocks.length; i++) {
        if (/Welcome to Physalia/.test(blocks[i].textContent || '')) { welcome = blocks[i]; break; }
      }
      out.welcomeFound = !!welcome;
      if (welcome) {
        var wr = welcome.getBoundingClientRect();
        out.welcome = {
          top: Math.round(wr.top), bottom: Math.round(wr.bottom), height: Math.round(wr.height),
          inViewport: wr.top < window.innerHeight && wr.bottom > 0
        };
      }

      var face = document.querySelector('svg, img');
      if (face) {
        var fr = face.getBoundingClientRect();
        out.firstGraphic = { top: Math.round(fr.top), height: Math.round(fr.height) };
      }

      out.viewport = { w: window.innerWidth, h: window.innerHeight };
      out.bodyScrollTop = document.scrollingElement ? document.scrollingElement.scrollTop : -1;
    } catch (e) {
      out.thrown = String(e);
    }
    document.documentElement.setAttribute('data-diag-' + tag, JSON.stringify(out));
  }

  function boot() {
    if (!window.physalia || !window.physalia.setState) { setTimeout(boot, 30); return; }
    window.physalia.setHistory([]);
    window.physalia.setState({ %STATE% });
    // Measure after Svelte flushes and after layout settles.
    setTimeout(function () { report('initial'); }, 400);
    setTimeout(function () {
      var s = document.querySelector('.chat-scroll');
      if (s) { s.scrollTop = 1; s.dispatchEvent(new Event('scroll', { bubbles: true })); s.scrollTop = 0; }
      window.dispatchEvent(new Event('resize'));
      setTimeout(function () { report('afterscroll'); }, 300);
    }, 900);
  }
  boot();
})();
</script>
"""

html = io.open(DIST, encoding='utf-8').read() if False else open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
assert sep, 'no </body> found'
open(OUT, 'w', encoding='utf-8').write(head + DRIVER.replace('%STATE%', STATE) + sep + tail)
print('wrote', OUT)
