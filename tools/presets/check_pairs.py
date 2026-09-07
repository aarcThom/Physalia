# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Confirm every wireless Feedback pair still points at a Collector that exists, in every preset,
AFTER the id reissue a preset load performs.

This is the one thing that would break silently and completely: a Feedback whose collector guid no
longer resolves swallows the signal and hands it nowhere, so a reply never reaches the Conversation
Log and the pipeline just stops. Nothing errors.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

import glob
import os

hc = _type("Physalia.GH.Harness.HarnessComponent")
readfile = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            if c.Name == "ReadDocumentFile" and c.GetParameters().Length == 2][0]

broken = 0
pairs = 0
for path in sorted(glob.glob(r"C:\Users\rober\repos\Physalia\wip_presets\*.phy")):
    name = os.path.basename(path)
    doc = readfile.Invoke(None, System.Array[System.Object]([path, None]))
    if doc is None:
        say("!! %s unreadable" % name)
        broken += 1
        continue

    def check(d, where):
        global broken, pairs
        ids = set(str(o.InstanceGuid) for o in d.Objects)
        collectors = set(str(o.InstanceGuid) for o in d.Objects if o.Name == "Feedback Collector")
        for o in d.Objects:
            if o.Name == "Feedback":
                # Collectors is exposed on the component; fall back to the private field if not.
                guids = None
                pr = o.GetType().GetProperty("Collectors",
                                             BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                if pr is not None:
                    guids = [str(g) for g in pr.GetValue(o)]
                else:
                    for f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic):
                        if "ollector" in f.Name:
                            guids = [str(g) for g in f.GetValue(o)]
                if guids is None:
                    say("  ?? %s could not be read" % (o.NickName or o.Name))
                    broken += 1
                    continue
                if not guids:
                    say("  BROKEN %s%s has NO collector" % (where, o.NickName or o.Name))
                    broken += 1
                for g in guids:
                    pairs += 1
                    if g not in collectors:
                        kind = "not a Collector" if g in ids else "does not exist"
                        say("  BROKEN %s%s -> %s (%s)" % (where, o.NickName or o.Name, g[:8], kind))
                        broken += 1
            if o.Name == "Harness" and o.InnerDocument is not None:
                check(o.InnerDocument, where + "inner/")

    before = broken
    check(doc, "")
    say("%-42s %s" % (name, "all pairs resolve" if broken == before else "PROBLEM"))
    doc.Dispose()

say("---- %d pairs checked, %d broken ----" % (pairs, broken))
