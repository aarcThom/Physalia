"""Drive the silent-update notice, and the version line under the logo.

Rhino 8's Package Manager updates installed plug-ins at startup with no prompt, so the host compares
the running build against the one recorded on this machine and hands the page a notice when they
differ. Two things here can only be proved from outside the page:

  * dismissing must SEND `phbridge://update-seen` — the host's record is what stops the notice
    coming back, and a page that merely hid the dialog would show it again on every restart;
  * "Don't tell me about updates" must send a DIFFERENT thing (`again=0`), because it is a different
    decision from "I have read this one".

`window.location` cannot be redefined from the page, so the navigation is captured over the DevTools
Protocol the way test_trigger_send.py does: Chrome reports the blocked custom-scheme navigation and
the report carries the URI.

The version line is checked at the same time, geometrically — it must sit UNDER the critter rather
than merely be in the DOM.
"""
import io
import json
import os
import re
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cdp  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
DIST = os.path.join(HERE, '..', '..', 'src', 'Physalia.UI', 'dist', 'index.html')
OUT = os.path.join(HERE, 'preview_update_notice.html')

NOTES = '- Your work moved to %LOCALAPPDATA%.\n- The entry screen shows the build.'

FOLDER = r'C:\Users\me\AppData\Local\Physalia'

VERSION = {'display': '1.1', 'full': '1.1.0.0',
           'update': {'from': '1.0', 'to': '1.1', 'notes': NOTES, 'folder': FOLDER}}

# Home, configured, nothing wired: the ConnectOptions entry screen, which is one of the two places
# the version line lives.
STATE = {
    'connected': False, 'busy': False, 'needsSetup': False, 'home': True, 'status': '',
    'configuredProviders': ['anthropic'], 'providerStatuses': [], 'groundingWired': False,
    'groundingTree': [], 'groundingSelection': None, 'exposeSignatures': False,
    'availableComponents': [], 'clustersWired': False, 'availableClusters': [],
    'clusterSelection': None, 'toolsWired': False, 'availableTools': [], 'toolsSelection': None,
    'referencedGeometryWired': False, 'availableReferencedGeometry': [], 'pythonWired': False,
    'pythonFunctions': [], 'unitsWired': False, 'documentUnits': 'Meters', 'unitsOverride': None,
    'unitOptions': ['Meters'], 'snapshotWired': False, 'snapshotGeometryPresent': False,
    'snapshotSendsMessage': False, 'snapshotDefaultMessage': '', 'snapshotMessage': None,
    'viewSnapshotWired': False, 'viewSnapshotSendsMessage': False,
    'viewSnapshotDefaultMessage': '', 'viewSnapshotMessage': None, 'imageToolWired': False,
    'exportToolWired': False, 'signalTraceToolWired': False, 'markUpToolWired': False,
    'tokenCountToolWired': False, 'pdfToolWired': False, 'pendingPdfs': [],
}

PROBE = """(function () {
  var dialog = document.querySelector('[role="dialog"][aria-modal="true"]');
  var line = null;
  document.querySelectorAll('p').forEach(function (p) {
    if (!line && /^v\\d/.test(p.textContent.trim())) { line = p; }
  });
  var svg = document.querySelector('svg[viewBox="0 0 317.78 446.09"]');
  var out = {
    dialog: !!dialog,
    versionText: line ? line.textContent.trim() : '',
    versionTitle: line ? (line.getAttribute('title') || '') : '',
    belowTheCritter: !!(line && svg) &&
      line.getBoundingClientRect().top >= svg.getBoundingClientRect().bottom - 1,
    buttons: []
  };
  if (dialog) {
    out.text = dialog.textContent.replace(/\\s+/g, ' ').trim();
    // The explanation on its own, so "keep it short" is something the rig can actually hold to.
    var para = dialog.querySelector('p');
    out.paragraph = para ? para.textContent.replace(/\\s+/g, ' ').trim() : '';
    out.notesRendered = !!dialog.querySelector('ul li');
    out.buttons = Array.prototype.map.call(dialog.querySelectorAll('button'),
      function (b) { return b.textContent.trim(); });
    var r = dialog.getBoundingClientRect();
    var mid = document.elementFromPoint(r.left + r.width / 2, r.top + r.height / 2);
    out.dialogIsOnTop = mid ? dialog.contains(mid) : false;
  }
  return JSON.stringify(out);
})()"""

CLICK = """(function () {
  var hit = false;
  document.querySelectorAll('[role="dialog"] button').forEach(function (b) {
    if (!hit && b.textContent.trim().indexOf(%s) >= 0) { b.click(); hit = true; }
  });
  return hit;
})()"""


def build():
    html = io.open(DIST, encoding='utf-8').read()
    head, sep, tail = html.rpartition('</body>')
    io.open(OUT, 'w', encoding='utf-8').write(head + '<script></script>' + sep + tail)


def drain(ws, seconds):
    """Collect every EVENT frame (no 'id') for a while. cdp.call discards these."""
    events = []
    deadline = time.time() + seconds
    while time.time() < deadline:
        try:
            ws.sock.settimeout(max(0.05, deadline - time.time()))
            msg = ws.recv()
        except Exception:
            break
        if msg and 'id' not in msg:
            events.append(msg)
    return events


def uris(events):
    # The log frames carry the URI twice — once as the blocked navigation, once inside the JS source
    # that triggered it, where it picks up a trailing quote. Trimmed and de-duplicated, so the
    # assertion is about the URI rather than about how Chrome reported it.
    found = re.findall(r'phbridge://[^\s"\\]+', json.dumps(events))
    return sorted({u.rstrip("'\"") for u in found})


def probe(ws):
    return json.loads(cdp.js(ws, PROBE))


def main():
    build()
    proc, ws = cdp.launch('file:///' + OUT.replace('\\', '/'))
    try:
        ws.call('Runtime.enable')
        ws.call('Log.enable')
        ws.call('Page.enable')
        # The real window, because the dialog has to fit inside it — see the note in
        # headless-chat-ui-testing about --window-size not setting the layout viewport.
        ws.call('Emulation.setDeviceMetricsOverride',
                {'width': 460, 'height': 620, 'deviceScaleFactor': 1, 'mobile': False})
        time.sleep(1.0)

        cdp.js(ws, 'window.physalia.setState(%s); window.physalia.setHistory([]); 1' % json.dumps(STATE))
        cdp.js(ws, 'window.physalia.setVersion(%s); 1' % json.dumps(VERSION))
        time.sleep(0.6)

        shown = probe(ws)
        cdp.screenshot(ws, os.path.join(HERE, 'update_notice.png'))

        drain(ws, 0.3)
        cdp.js(ws, CLICK % json.dumps('Got it'))
        got_it = uris(drain(ws, 1.5))
        time.sleep(0.4)
        after = probe(ws)

        # A fresh push is a notice the host still wants shown, so the dialog must come back — then
        # the second action must send something different.
        cdp.js(ws, 'window.physalia.setVersion(%s); 1' % json.dumps(VERSION))
        time.sleep(0.5)
        reopened = probe(ws)
        drain(ws, 0.3)
        cdp.js(ws, CLICK % json.dumps("Don't tell me"))
        opt_out = uris(drain(ws, 1.5))
        time.sleep(0.4)

        # No update in the push: the version line stays, the dialog does not appear.
        cdp.js(ws, 'window.physalia.setVersion(%s); 1'
               % json.dumps({'display': '1.1', 'full': '1.1.0.0', 'update': None}))
        time.sleep(0.5)
        quiet = probe(ws)
        cdp.screenshot(ws, os.path.join(HERE, 'update_notice_dismissed.png'))

        checks = {
            'dialog appears on an update': shown['dialog'],
            'it names both versions':
                '1.0' in shown.get('text', '') and '1.1' in shown.get('text', ''),
            'it says where the user\'s own work is kept':
                FOLDER in shown.get('text', ''),
            'it stays short': len(shown.get('paragraph', '')) < 200,
            'the changelog section is rendered as markdown': shown.get('notesRendered') is True,
            'it is drawn above the page': shown.get('dialogIsOnTop') is True,
            'it offers both actions': len(shown.get('buttons', [])) >= 2,
            'the version line reads v1.1': shown['versionText'] == 'v1.1',
            'the full build is on its tooltip': shown['versionTitle'] == 'Physalia 1.1.0.0',
            'the line sits under the critter': shown['belowTheCritter'],
            'Got it acknowledges to the host': got_it == ['phbridge://update-seen?again=1'],
            'Got it closes the dialog': not after['dialog'],
            'a re-push shows it again': reopened['dialog'],
            'opting out sends a different thing': opt_out == ['phbridge://update-seen?again=0'],
            'no update means no dialog': not quiet['dialog'],
            'the version line survives without an update': quiet['versionText'] == 'v1.1',
        }

        print(json.dumps({'shown': shown, 'gotIt': got_it, 'optOut': opt_out}, indent=2))
        for name, ok in checks.items():
            print(('PASS  ' if ok else 'FAIL  ') + name)

        return 0 if all(checks.values()) else 1
    finally:
        try:
            ws.close()
        except Exception:
            pass
        proc.kill()


if __name__ == '__main__':
    sys.exit(main())
