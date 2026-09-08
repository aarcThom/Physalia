# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Rebuild every preset and run both whole-set checks, in one go.

Progress is written to a LOG FILE rather than returned, because this takes longer than an MCP call
is allowed and stdout dies with the process anyway. Run it, then read the log:

    <your system temp dir>/claude/build_all.log

Each build script clears the host canvas first, so they can run back to back in one session: the
harness for preset N replaces the one for N-1, and only the written .phy survives. A build that
throws is reported and the rest still run - one broken script should not cost you the rest.

Both families are rebuilt: `build_NN_*` (the numbered presets) then `build_sNN_*` (the scenarios).
"""

import glob
import os
import tempfile
import traceback

# The ONE machine-specific line in this file. Set ROOT before exec'ing to build from a checkout
# somewhere else:  ROOT = r"D:\code\Physalia"
# This script does NOT exec phybuild, so it cannot use PRESETS/SCRATCH from there.
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"

HERE = os.path.join(ROOT, "tools", "presets")
LOG = os.path.join(tempfile.gettempdir(), "claude", "build_all.log")
if not os.path.isdir(os.path.dirname(LOG)):
    os.makedirs(os.path.dirname(LOG))

_lines = []


def note(text):
    _lines.append(text)
    with open(LOG, "w") as f:
        f.write("\n".join(_lines))


note("rebuilding every preset - numbered, then scenarios")
# Numbered first, then scenarios - the same order the README lists them in, so the log reads
# alongside it. build_all itself is excluded by both patterns.
scripts = (sorted(glob.glob(os.path.join(HERE, "build_[0-9][0-9]_*.py")))
           + sorted(glob.glob(os.path.join(HERE, "build_s[0-9][0-9]_*.py"))))
failed = []
for script in scripts:
    name = os.path.basename(script)
    try:
        # Its own namespace, so one script's leftovers cannot be read as another's - but keep a
        # handle on it, because each build leaves its PROBLEMS count in `bad` and that is the
        # number worth reporting.
        # ROOT must be pushed in, or each script falls back to ITS OWN default and a run
        # from another checkout silently rebuilds the wrong repo.
        scope = {"__name__": "__main__", "ROOT": ROOT}
        exec(compile(open(script).read(), script, "exec"), scope)
        problems = scope.get("bad", "?")
        note("  %-28s built, %s problem(s)" % (name, problems))
        if problems != 0:
            failed.append("%s: %s problems" % (name, problems))
    except Exception as ex:
        note("  %-28s FAILED: %s" % (name, ex))
        note("      " + traceback.format_exc().replace("\n", "\n      "))
        failed.append("%s: %s" % (name, ex))

note("")
note("---- whole-set checks ----")
# Only check_pairs runs here: it must resolve guids inside a loaded document, so it needs Rhino.
# audit.py deliberately does NOT - it reads the package bytes - so it is a shell command, and
# exec'ing it in here would fail anyway since it uses __file__ to find the presets.
for check in ("check_pairs.py",):
    path = os.path.join(HERE, check)
    try:
        # ROOT must be pushed in, or each script falls back to ITS OWN default and a run
        # from another checkout silently rebuilds the wrong repo.
        scope = {"__name__": "__main__", "ROOT": ROOT}
        exec(compile(open(path).read(), path, "exec"), scope)
        found = scope.get("broken", scope.get("total", "?"))
        note("  %-16s ran, %s finding(s) - full output is in Rhino above" % (check, found))
        if found not in (0, "?"):
            failed.append("%s: %s findings" % (check, found))
    except Exception as ex:
        note("  %s FAILED: %s" % (check, ex))
        failed.append("%s: %s" % (check, ex))

note("")
note("now run the other check from a shell, outside Rhino:")
note("    python tools/presets/audit.py")
note("")
note("BUILT %d preset(s), %d problem(s)" % (len(scripts), len(failed)))
for f in failed:
    note("  " + f)
print("\n".join(_lines))
