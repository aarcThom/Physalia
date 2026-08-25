"""PDF intake, driven headlessly against the real bundle: the rail button and the chip strip.

Guards the bug this was written for. `AddPdf` was wired end to end and correct on the C# side, but
`App.svelte` used `<FileTextIcon />` without importing it, so rendering the button threw
`ReferenceError` — which, happening inside Svelte's render, took the rest of the pass with it and
made the whole window look half-built and frozen. Nothing in the build log, and `svelte-check`
reported zero errors.

So the assertion that matters most here is **`window.__errs` being empty**: a missing button is the
symptom, but the thrown error is the disease, and it is what breaks everything else on the page.
`check_components.py` catches this particular class statically and much faster; this covers the rest
of the wiring — that a pushed `pdfToolWired` really does light the button, and that `pendingPdfs`
really does render as chips.

    python tools/uitest/test_pdf_intake.py            # builds its own page from dist/index.html
    python tools/uitest/test_pdf_intake.py shot.png   # …and saves a screenshot
"""
import os
import pathlib
import sys
import tempfile
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cdp  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
BUNDLE = ROOT / 'src' / 'Physalia.UI' / 'dist' / 'index.html'

STATE = """{
  connected:true, busy:false, needsSetup:false, home:false, status:'',
  configuredProviders:['anthropic'], groundingWired:false, groundingTree:[],
  groundingSelection:null, exposeSignatures:false, availableComponents:[],
  clustersWired:false, availableClusters:[], clusterSelection:null,
  toolsWired:false, availableTools:[], toolsSelection:null,
  referencedGeometryWired:false, availableReferencedGeometry:[],
  pythonWired:false, pythonFunctions:[], unitsWired:false, documentUnits:'Meters',
  unitsOverride:null, unitOptions:['Meters'], snapshotWired:false,
  snapshotGeometryPresent:false, snapshotSendsMessage:true, snapshotDefaultMessage:'',
  snapshotMessage:null, viewSnapshotWired:false, viewSnapshotSendsMessage:true,
  viewSnapshotDefaultMessage:'', viewSnapshotMessage:null, imageToolWired:true,
  exportToolWired:false, signalTraceToolWired:false, markUpToolWired:false,
  tokenCountToolWired:false, pdfToolWired:true,
  pendingPdfs:[{alias:'a-101',name:'A-101 Floor Plans.pdf',pages:24},
               {alias:'site-survey',name:'Site Survey.pdf',pages:3}]
}"""

SCRIPT = """
<script>
window.__errs = [];
window.addEventListener('error', function (e) { window.__errs.push(String(e.message)); });
window.addEventListener('unhandledrejection', function (e) { window.__errs.push('rejected: ' + e.reason); });
window.chrome = { webview: { postMessage: function (m) { window.__sent = m; } } };
setTimeout(function () {
  try {
    window.physalia.setState(%STATE%);
    window.physalia.setHistory([]);
  } catch (e) { window.__errs.push('setState threw: ' + e); }
}, 500);
</script>
"""

BUTTON = ("Array.from(document.querySelectorAll('button'))"
          ".filter(function (b) { return (b.getAttribute('title') || '') === '%s'; }).length")
CHIPS = ("Array.from(document.querySelectorAll('div[title]'))"
         ".map(function (d) { return d.getAttribute('title'); })"
         ".filter(function (t) { return t.indexOf('.pdf') >= 0; })")


def build_page():
    html = BUNDLE.read_text(encoding='utf-8')
    # rpartition, not replace: '</body>' also appears inside the inlined app JS.
    head, sep, tail = html.rpartition('</body>')
    if not sep:
        raise SystemExit('bundle has no </body> — is dist/index.html built?')
    path = os.path.join(tempfile.mkdtemp(prefix='pdfui-'), 'page.html')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(head + SCRIPT.replace('%STATE%', STATE) + sep + tail)
    return path


def main():
    if not BUNDLE.exists():
        raise SystemExit('dist/index.html missing — run: dotnet build src/Physalia.slnx -c Debug')

    page = build_page()
    proc, ws = cdp.launch('file:///' + page.replace('\\', '/'))
    failures = []
    try:
        time.sleep(3.0)

        errs = cdp.js(ws, 'window.__errs') or []
        if errs:
            failures.append('JS errors during render: %s' % errs)

        if cdp.js(ws, BUTTON % 'Attach PDF') != 1:
            failures.append('the Attach PDF rail button did not render')
        if cdp.js(ws, BUTTON % 'Add image') != 1:
            failures.append('the Add image button vanished — a render abort takes neighbours with it')

        chips = cdp.js(ws, CHIPS) or []
        if len(chips) != 2:
            failures.append('expected 2 PDF chips, got %d: %s' % (len(chips), chips))
        if not any('24' in c for c in chips):
            failures.append('a chip is not showing its page count: %s' % chips)
        if cdp.js(ws, BUTTON % 'Remove PDF') != 2:
            failures.append('each chip should carry its own remove button')

        if len(sys.argv) > 1:
            cdp.screenshot(ws, sys.argv[1])
            print('screenshot ->', sys.argv[1])
    finally:
        proc.kill()

    for line in failures:
        print('FAIL  ' + line)
    print('PDF intake: %s' % ('FAILED' if failures else 'ok'))
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
