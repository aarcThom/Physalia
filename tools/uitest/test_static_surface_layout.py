"""Check that a static surface (setup / a header page) gets the whole window.

The prompt box must NOT be rendered when there is nothing to send a message to, and the surface's
scroller must then run down to where the box used to be. Drives two states in one page: first-run
setup (no composer expected), then a connected conversation (composer expected).

Insert the driver before the LAST </body> — the inlined app JS contains that string too.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT = sys.argv[1]

COMMON = """
    groundingWired: false, groundingTree: [],
    groundingSelection: null, exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: false, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters'], snapshotWired: false,
    snapshotGeometryPresent: false, snapshotSendsMessage: false, snapshotDefaultMessage: '',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: false,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: true,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: false,
    tokenCountToolWired: false, pdfToolWired: false, pendingPdfs: []
"""

SETUP_STATE = """
    connected: false, busy: false, needsSetup: true, home: false, status: 'Setup mode',
    configuredProviders: ['codex'],
    providerStatuses: [
      { id: 'codex', activated: true, source: 'cli', detail: null },
      { id: 'anthropic', activated: false, source: 'none', detail: null }
    ],
""" + COMMON

CHAT_STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['codex'],
    providerStatuses: [{ id: 'codex', activated: true, source: 'cli', detail: null }],
""" + COMMON

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
      var editor = document.querySelector('[contenteditable="true"], textarea');
      out.composerPresent = !!editor;

      // The action stack lives beside the box: the send button carries title="Send".
      out.sendButtonPresent = !!document.querySelector('button[title="Send"]');
      out.trashPresent = !!document.querySelector('button[title^="Clear signals"]');

      var scroller = document.querySelector('.chat-scroll');
      out.scrollerFound = !!scroller;
      if (scroller) {
        var sr = scroller.getBoundingClientRect();
        out.scroller = {
          top: Math.round(sr.top), bottom: Math.round(sr.bottom),
          height: Math.round(sr.height),
          scrollTop: scroller.scrollTop, scrollHeight: scroller.scrollHeight,
          clientHeight: scroller.clientHeight
        };
        out.gapToWindowBottom = Math.round(window.innerHeight - sr.bottom);
      }

      // The back control: label and whether it is a raised button (neu-btn) or flat (neu-ghost).
      var back = null;
      var buttons = document.querySelectorAll('button');
      for (var i = 0; i < buttons.length; i++) {
        if (/Go Back/.test(buttons[i].textContent || '')) { back = buttons[i]; break; }
      }
      out.goBackFound = !!back;
      out.backToChatFound = /Back to chat/.test(document.body.textContent || '');
      if (back) {
        out.goBackRaised = back.className.indexOf('neu-btn') >= 0;
        out.goBackGhost = back.className.indexOf('neu-ghost') >= 0;
      }

      out.viewport = { w: window.innerWidth, h: window.innerHeight };
    } catch (e) {
      out.thrown = String(e);
    }
    document.documentElement.setAttribute('data-diag-' + tag, JSON.stringify(out));
  }

  function boot() {
    if (!window.physalia || !window.physalia.setState) { setTimeout(boot, 30); return; }
    window.physalia.setHistory([]);
    window.physalia.setState({ %SETUP% });
    setTimeout(function () { report('setup'); }, 400);
    setTimeout(function () {
      window.physalia.setHistory([{ id: 'm1', role: 'user', text: 'hello', tools: [] }]);
      window.physalia.setState({ %CHAT% });
      setTimeout(function () { report('chat'); }, 400);
    }, 900);
  }
  boot();
})();
</script>
"""

html = open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
assert sep, 'no </body> found'
driver = DRIVER.replace('%SETUP%', SETUP_STATE).replace('%CHAT%', CHAT_STATE)
open(OUT, 'w', encoding='utf-8').write(head + driver + sep + tail)
print('wrote', OUT)
