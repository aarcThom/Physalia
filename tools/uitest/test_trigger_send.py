"""Confirm WHAT the Trigger Control page sends the host when its switches are pressed.

The DOM test (`test_trigger_control.py`) proves the page renders and the buttons exist. This proves
the part that actually matters and that a screenshot cannot show: a per-row switch must send
`armtrigger?id=<guid>&on=…` — addressed by INSTANCE ID, since two triggers can share a nickname —
while "Switch all off" must send `armtriggers?on=0`, a different verb, because switching everything
off discards what a recorder has gathered and switching one off hands it over.

`window.location` cannot be redefined from the page (Chrome refuses: "Cannot redefine property"), so
the navigation is captured from OUTSIDE, over the DevTools Protocol. Chrome reports an attempt to
navigate to an unregistered scheme, and that report carries the URI.
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
OUT = os.path.join(HERE, 'preview_trigger_send.html')

TRIGGERS = [
    {'id': '11111111-1111-1111-1111-111111111111', 'kind': 'Timer', 'name': 'Timer',
     'armed': True, 'caption': 'every 30s', 'handsOver': False},
    {'id': '22222222-2222-2222-2222-222222222222', 'kind': 'Folder Watcher', 'name': 'Watch',
     'armed': False, 'caption': 'off', 'handsOver': False},
    {'id': '33333333-3333-3333-3333-333333333333', 'kind': 'Watch Modelling', 'name': 'Watch',
     'armed': True, 'caption': 'recording', 'handsOver': True},
]

STATE = {
    'connected': True, 'busy': False, 'needsSetup': False, 'home': False, 'status': '',
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
    'triggerControlWired': True, 'triggers': TRIGGERS,
}


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
    """Every phbridge:// URI mentioned anywhere in a batch of events."""
    blob = json.dumps(events)
    return re.findall(r'phbridge://[^\s"\\]+', blob)


def main():
    build()
    proc, ws = cdp.launch('file:///' + OUT.replace('\\', '/'))
    try:
        ws.call('Runtime.enable')
        ws.call('Log.enable')
        ws.call('Page.enable')
        time.sleep(1.0)

        cdp.js(ws, 'window.physalia.setState(%s); window.physalia.setHistory([]); 1'
               % json.dumps(STATE))
        time.sleep(0.5)

        opened = cdp.js(ws, """(function(){
            var hit = false;
            document.querySelectorAll('header button').forEach(function (b) {
              if (!hit && (b.getAttribute('title') || '').indexOf('trigger list') >= 0) {
                b.click(); hit = true;
              }
            });
            return hit;
        })()""")
        time.sleep(0.5)

        rows = cdp.js(ws, "document.querySelectorAll('button.neu-raised').length")

        # Row 2 is the OFF Folder Watcher, so this must arm it — and must carry its id, not "Watch",
        # which it shares with the Watch Modelling row.
        drain(ws, 0.3)
        cdp.js(ws, "document.querySelectorAll('button.neu-raised')[1].click(); 1")
        row_uris = uris(drain(ws, 1.5))

        # Row 3 is the armed recorder: switching it off is the hand-over act.
        cdp.js(ws, "document.querySelectorAll('button.neu-raised')[2].click(); 1")
        rec_uris = uris(drain(ws, 1.5))

        cdp.js(ws, """(function(){
            var hit = false;
            document.querySelectorAll('button').forEach(function (b) {
              if (!hit && (b.textContent || '').indexOf('Switch all off') >= 0) { b.click(); hit = true; }
            });
            return hit;
        })()""")
        all_uris = uris(drain(ws, 1.5))

        result = {
            'opened': opened,
            'rows': rows,
            'rowClick': row_uris[-1] if row_uris else '',
            'recorderClick': rec_uris[-1] if rec_uris else '',
            'switchAllOff': all_uris[-1] if all_uris else '',
        }

        checks = {
            'row arms by id':
                result['rowClick'].startswith(
                    'phbridge://armtrigger?id=22222222-2222-2222-2222-222222222222&on=1'),
            'recorder disarms by id':
                result['recorderClick'].startswith(
                    'phbridge://armtrigger?id=33333333-3333-3333-3333-333333333333&on=0'),
            'switch-all uses its own verb':
                result['switchAllOff'].startswith('phbridge://armtriggers?on=0'),
            'no name ever sent':
                'Watch' not in result['rowClick'] + result['recorderClick'] + result['switchAllOff'],
        }

        print(json.dumps(result, indent=2))
        print('')
        for name, ok in checks.items():
            print('%s %s' % ('PASS' if ok else 'FAIL', name))
        return 0 if all(checks.values()) else 1
    finally:
        proc.kill()


if __name__ == '__main__':
    sys.exit(main())
