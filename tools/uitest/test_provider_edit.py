"""Drive the setup page's CONFIGURED-provider path: the pill that opens it, the reconfigure form,
and the two-step disconnect.

What this covers, and why each part is worth measuring rather than reading:

* A configured provider's pill must be a BUTTON. It was a plain label, which made a configured
  provider the one thing on that screen with no way back into it — the connected footer existed and
  was unreachable.
* Opening one must prefill the URL box from the endpoint IN EFFECT, not the catalog default. The
  page is handed `baseUrl` on the status; if it ever falls back to `providers.ts` the box will still
  look plausible and will quietly save the wrong host back.
* The key box must be blank with a placeholder saying the saved key is kept. A blank box that meant
  "clear it" would destroy a credential on an endpoint-only edit.
* Disconnect must ask twice when a key is on disk and go straight through when there is none.

Nothing here navigates: the stored-key Disconnect only opens the confirm row, so no phbridge:// URL
is ever set. Insert the driver before the LAST </body> — the inlined app JS contains that string too.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT = sys.argv[1]

# needsSetup keeps the setup surface on screen; the configured list is what puts pills on it.
# Anthropic carries a MOVED endpoint and a stored key; Claude Code stores nothing at all.
STATE = """
    connected: false, busy: false, needsSetup: true, home: false, status: 'Setup mode',
    configuredProviders: ['claude-code', 'anthropic'],
    providerStatuses: [
      { id: 'claude-code', activated: true, source: 'detected', detail: null,
        baseUrl: null, hasStoredKey: false },
      { id: 'anthropic', activated: true, source: 'stored', detail: null,
        baseUrl: 'https://gateway.internal/v1', hasStoredKey: true }
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

  function texts(sel) {
    return Array.prototype.map.call(document.querySelectorAll(sel), function (el) {
      return (el.textContent || '').replace(/\\s+/g, ' ').trim();
    });
  }

  function buttonSaying(re) {
    var all = document.querySelectorAll('button');
    for (var i = 0; i < all.length; i++) {
      if (re.test((all[i].textContent || '').replace(/\\s+/g, ' ').trim())) return all[i];
    }
    return null;
  }

  // Horizontal overflow is the failure mode a screenshot shows and a DOM assertion misses: a row of
  // buttons that will not wrap pushes the whole page sideways in a 460px window.
  function widths() {
    var doc = document.scrollingElement || document.documentElement;
    var scroller = document.querySelector('.chat-scroll');
    return {
      docScrollWidth: doc.scrollWidth, docClientWidth: doc.clientWidth,
      scrollerScrollWidth: scroller ? scroller.scrollWidth : -1,
      scrollerClientWidth: scroller ? scroller.clientWidth : -1
    };
  }

  function put(tag, out) {
    out.errors = errors;
    out.widths = widths();
    document.documentElement.setAttribute('data-diag-' + tag, JSON.stringify(out));
  }

  // --- the picker: are the configured providers reachable at all? ---------------------------
  function reportPills() {
    var out = {};
    try {
      var pills = [];
      var all = document.querySelectorAll('button, span');
      for (var i = 0; i < all.length; i++) {
        var t = (all[i].textContent || '').replace(/\\s+/g, ' ').trim();
        if (t === 'Anthropic' || t === 'Claude Code (subscription)') {
          pills.push({
            label: t,
            tag: all[i].tagName,
            clickable: all[i].tagName === 'BUTTON',
            hasIcon: !!all[i].querySelector('svg')
          });
        }
      }
      out.pills = pills;
    } catch (e) { out.thrown = String(e); }
    put('pills', out);
  }

  // --- the connected page: reconfigure form + disconnect ------------------------------------
  function reportConnected() {
    var out = {};
    try {
      var url = document.getElementById('setup-url');
      var key = document.getElementById('setup-key');
      out.urlValue = url ? url.value : null;
      out.keyValue = key ? key.value : null;
      out.keyPlaceholder = key ? key.getAttribute('placeholder') : null;
      out.keyType = key ? key.getAttribute('type') : null;
      out.hasSaveChanges = !!buttonSaying(/^Save changes$/);
      out.hasDisconnect = !!buttonSaying(/^Disconnect$/);
      // The install guide must be gone: it exists to get someone TO this point.
      out.stepCount = document.querySelectorAll('ol li').length;
      out.body = (document.querySelector('.chat-scroll') || document.body).textContent
        .replace(/\\s+/g, ' ').trim().slice(0, 400);
    } catch (e) { out.thrown = String(e); }
    put('connected', out);
  }

  function reportConfirm() {
    var out = {};
    try {
      out.hasDeleteKey = !!buttonSaying(/Disconnect & delete key/);
      out.hasCancel = !!buttonSaying(/^Cancel$/);
      out.stillHasPlainDisconnect = !!buttonSaying(/^Disconnect$/);
      out.body = (document.querySelector('.chat-scroll') || document.body).textContent
        .replace(/\\s+/g, ' ').trim().slice(0, 400);
    } catch (e) { out.thrown = String(e); }
    put('confirm', out);
  }

  // --- a DETECTED provider: nothing stored, so nothing to reconfigure and nothing to confirm ---
  function reportDetected() {
    var out = {};
    try {
      out.hasUrlBox = !!document.getElementById('setup-url');
      out.hasKeyBox = !!document.getElementById('setup-key');
      out.hasDisconnect = !!buttonSaying(/^Disconnect$/);
      out.body = (document.querySelector('.chat-scroll') || document.body).textContent
        .replace(/\\s+/g, ' ').trim().slice(0, 400);
    } catch (e) { out.thrown = String(e); }
    put('detected', out);
  }

  // --- and the ORIGINAL path, unchanged by all of the above: a provider set up for the first time.
  // The form moved into a snippet shared with the reconfigure footer, so this is the regression that
  // refactor could have caused.
  function reportUnconfigured() {
    var out = {};
    try {
      var key = document.getElementById('setup-key');
      out.hasUrlBox = !!document.getElementById('setup-url');
      out.hasKeyBox = !!key;
      out.keyPlaceholder = key ? key.getAttribute('placeholder') : null;
      out.urlValue = (document.getElementById('setup-url') || {}).value;
      out.hasPlainSave = !!buttonSaying(/^Save$/);
      out.hasSaveChanges = !!buttonSaying(/^Save changes$/);
      out.hasDisconnect = !!buttonSaying(/^Disconnect$/);
      out.stepCount = document.querySelectorAll('ol li').length;
    } catch (e) { out.thrown = String(e); }
    put('unconfigured', out);
  }

  function boot() {
    if (!window.physalia || !window.physalia.setState) { setTimeout(boot, 30); return; }
    window.physalia.setHistory([]);
    window.physalia.setState({ %STATE% });

    setTimeout(function () {
      reportPills();
      var pill = buttonSaying(/^Anthropic$/);
      if (pill) { pill.click(); }
      // Never measure in the click's own tick — Svelte has not flushed.
      setTimeout(function () {
        reportConnected();
        var disconnect = buttonSaying(/^Disconnect$/);
        if (disconnect) { disconnect.click(); }
        setTimeout(function () {
          reportConfirm();
          var cancel = buttonSaying(/^Cancel$/);
          if (cancel) { cancel.click(); }
          var back = buttonSaying(/^Go Back$/);
          if (back) { back.click(); }
          setTimeout(function () {
            var cc = buttonSaying(/^Claude Code \\(subscription\\)$/);
            if (cc) { cc.click(); }
            setTimeout(function () {
              reportDetected();
              var back2 = buttonSaying(/^Go Back$/);
              if (back2) { back2.click(); }
              setTimeout(function () {
                var fresh = buttonSaying(/^OpenAI$/);
                if (fresh) { fresh.click(); }
                setTimeout(reportUnconfigured, 300);
              }, 300);
            }, 300);
          }, 300);
        }, 300);
      }, 350);
    }, 500);
  }
  boot();
})();
</script>
"""

html = open(DIST, encoding='utf-8').read()
head, sep, tail = html.rpartition('</body>')
assert sep, 'no </body> found'
open(OUT, 'w', encoding='utf-8').write(head + DRIVER.replace('%STATE%', STATE) + sep + tail)
print('wrote', OUT)
