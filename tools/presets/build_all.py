# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Rebuild every preset and run both whole-set checks, in one go.

Progress is written to a LOG FILE rather than returned, because this takes longer than an MCP call
is allowed and stdout dies with the process anyway. Run it, then read the log:

    C:/Users/rober/AppData/Local/Temp/claude/build_all.log

Each build script clears the host canvas first, so they can run back to back in one session: the
harness for preset N replaces the one for N-1, and only the written .phy survives. A build that
throws is reported and the rest still run - one broken script should not cost you the other
thirteen.
"""

import glob
import os
import traceback

HERE = r"C:\Users\rober\repos\Physalia\tools\presets"
LOG = r"C:\Users\rober\AppData\Local\Temp\claude\build_all.log"

_lines = []


def note(text):
    _lines.append(text)
    with open(LOG, "w") as f:
        f.write("\n".join(_lines))


note("rebuilding every preset")
scripts = sorted(glob.glob(os.path.join(HERE, "build_[0-9][0-9]_*.py")))
failed = []
for script in scripts:
    name = os.path.basename(script)
    try:
        # Its own namespace, so one script's leftovers cannot be read as another's - but keep a
        # handle on it, because each build leaves its PROBLEMS count in `bad` and that is the
        # number worth reporting.
        scope = {"__name__": "__main__"}
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
for check in ("audit.py", "check_pairs.py"):
    path = os.path.join(HERE, check)
    try:
        scope = {"__name__": "__main__"}
        exec(compile(open(path).read(), path, "exec"), scope)
        found = scope.get("broken", scope.get("total", "?"))
        note("  %-16s ran, %s finding(s) - full output is in Rhino above" % (check, found))
        if found not in (0, "?"):
            failed.append("%s: %s findings" % (check, found))
    except Exception as ex:
        note("  %s FAILED: %s" % (check, ex))
        failed.append("%s: %s" % (check, ex))

note("")
note("BUILT %d preset(s), %d problem(s)" % (len(scripts), len(failed)))
for f in failed:
    note("  " + f)
print("\n".join(_lines))
