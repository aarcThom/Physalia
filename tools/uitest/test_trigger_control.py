"""Drive the Trigger Control page in headless Chrome: does the rail button appear, does the page
list what it was pushed, and do the switches send what the host expects?

Checks the two things that are easy to get wrong and impossible to see in a screenshot:
  - a per-row switch must send `armtrigger?id=...` (the same act as the node's own menu, so a
    recorder HANDS OVER), while "Switch all off" must send `armtriggers?on=0` (the kill switch,
    which discards)
  - a trigger is addressed by INSTANCE ID, never by name, since two can share a nickname

The script MUST be inserted before the LAST </body> — the inlined app JS contains that string too,
and a global replace corrupts the bundle.
"""
import io
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
CHROME = r'C:\Program Files\Google\Chrome\Application\chrome.exe'
OUT = os.path.join(HERE, 'preview_triggers.html')

# Two triggers sharing the nickname "Watch" on purpose — Folder Watcher's default nickname IS
# "Watch", and so is Watch Modelling's. If anything keyed on the name, this is what would break it.
TRIGGERS = """[
  { id: '11111111-1111-1111-1111-111111111111', kind: 'Timer', name: 'Timer',
    armed: true, caption: 'every 30s \\u00b7 4', handsOver: false },
  { id: '22222222-2222-2222-2222-222222222222', kind: 'Folder Watcher', name: 'Watch',
    armed: false, caption: 'off', handsOver: false },
  { id: '33333333-3333-3333-3333-333333333333', kind: 'Watch Modelling', name: 'Watch',
    armed: true, caption: 'recording \\u00b7 7', handsOver: true }
]"""

STATE = """
    connected: true, busy: false, needsSetup: false, home: false, status: '',
    configuredProviders: ['anthropic'], providerStatuses: [], groundingWired: false,
    groundingTree: [], groundingSelection: null, exposeSignatures: false, availableComponents: [],
    clustersWired: false, availableClusters: [], clusterSelection: null,
    toolsWired: false, availableTools: [], toolsSelection: null,
    referencedGeometryWired: false, availableReferencedGeometry: [],
    pythonWired: false, pythonFunctions: [], unitsWired: false, documentUnits: 'Meters',
    unitsOverride: null, unitOptions: ['Meters'], snapshotWired: false,
    snapshotGeometryPresent: false, snapshotSendsMessage: false, snapshotDefaultMessage: '',
    snapshotMessage: null, viewSnapshotWired: false, viewSnapshotSendsMessage: false,
    viewSnapshotDefaultMessage: '', viewSnapshotMessage: null, imageToolWired: false,
    exportToolWired: false, signalTraceToolWired: false, markUpToolWired: false,
    tokenCountToolWired: false, pdfToolWired: false, pendingPdfs: [],
    triggerControlWired: true, triggers: %TRIGGERS%
"""

SCRIPT = """
<script>
window.__errs = [];
window.__nav = [];
window.addEventListener('error', function (e) { window.__errs.push(String(e.message)); });

// The page reaches the host by assigning window.location.href to a phbridge:// URI. Intercept it so
// the navigation is captured instead of attempted.
try {
  var real = window.location;
  Object.defineProperty(window, 'location', {
    configurable: true,
    get: function () {
      return {
        get href() { return real.href; },
        set href(v) { window.__nav.push(String(v)); },
        toString: function () { return real.href; }
      };
    },
    set: function (v) { window.__nav.push(String(v)); }
  });
} catch (e) { window.__errs.push('loc: ' + e.message); }

function byText(tag, text) {
  var hit = null;
  document.querySelectorAll(tag).forEach(function (el) {
    if ((el.textContent || '').trim().indexOf(text) >= 0 && !hit) { hit = el; }
  });
  return hit;
}

setTimeout(function () {
  window.physalia.setState({%STATE%});
  window.physalia.setHistory([]);

  setTimeout(function () {
    var diag = { errs: window.__errs.slice(0, 4) };

    // 1. the rail button, tinted because something is armed
    var rail = document.querySelector('header button.text-\\\\[var\\\\(--neu-accent\\\\)\\\\]');
    var railButtons = document.querySelectorAll('header button').length;
    diag.railButtons = railButtons;
    diag.railTinted = !!rail;

    // 2. open the page
    var opened = false;
    document.querySelectorAll('header button').forEach(function (b) {
      if (!opened && (b.getAttribute('title') || '').indexOf('trigger list') >= 0) {
        b.click(); opened = true;
      }
    });
    diag.opened = opened;

    setTimeout(function () {
      diag.heading = !!byText('h2', 'Triggers');
      diag.rows = document.querySelectorAll('button.neu-raised').length;
      var body = document.body.textContent || '';
      diag.saysCounts = body.indexOf('2 of 3 armed') >= 0;
      diag.saysCaption = body.indexOf('every 30s') >= 0 && body.indexOf('recording') >= 0;
      diag.warnsAboutDiscard = body.indexOf('discards what a recording has gathered') >= 0;
      diag.rowWarning = body.indexOf('sends what it has recorded') >= 0;

      // 3. click the SECOND row (the off Folder Watcher) -> should arm it, by id
      var rows = document.querySelectorAll('button.neu-raised');
      if (rows.length > 1) { rows[1].click(); }

      setTimeout(function () {
        diag.navAfterRow = window.__nav.slice(-1)[0] || '';

        // 4. "Switch all off" -> the kill switch, a different verb
        var all = byText('button', 'Switch all off');
        diag.foundSwitchAll = !!all;
        if (all) { all.click(); }

        setTimeout(function () {
          diag.navAfterAll = window.__nav.slice(-1)[0] || '';
          document.documentElement.setAttribute('data-diag', JSON.stringify(diag));
        }, 150);
      }, 150);
    }, 250);
  }, 300);
}, 400);
</script>
"""


def main():
    html = io.open(DIST, encoding='utf-8').read()
    script = SCRIPT.replace('%STATE%', STATE.replace('%TRIGGERS%', TRIGGERS))
    head, sep, tail = html.rpartition('</body>')
    io.open(OUT, 'w', encoding='utf-8').write(head + script + sep + tail)

    shot = os.path.join(HERE, 'preview_triggers.png')
    out = subprocess.run(
        [CHROME, '--headless=new', '--disable-gpu', '--virtual-time-budget=12000',
         '--window-size=460,620', '--screenshot=' + shot, '--dump-dom',
         'file:///' + OUT.replace('\\', '/')],
        capture_output=True, timeout=180)

    # Bytes, decoded permissively: the bundle contains characters cp1252 cannot decode, and Python's
    # default text mode picks cp1252 on this box.
    dom = out.stdout.decode('utf-8', 'replace')
    err = out.stderr.decode('utf-8', 'replace')

    marker = 'data-diag="'
    if marker not in dom:
        print('NO DIAG. stderr tail:')
        print(err[-1500:])
        return 1

    raw = dom.split(marker, 1)[1].split('"', 1)[0]
    import html as htmlmod
    print(htmlmod.unescape(raw))
    print('screenshot:', shot)
    return 0


if __name__ == '__main__':
    sys.exit(main())
