# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Confirm every wireless Feedback pair still points at a Collector that exists, in every preset,
AFTER the id reissue a preset load performs.

This is the one thing that would break silently and completely: a Feedback whose collector guid no
longer resolves swallows the signal and hands it nowhere, so a reply never reaches the Conversation
Log and the pipeline just stops. Nothing errors.
"""

# The ONE machine-specific line in this file. Set ROOT before exec'ing this script to build
# from a checkout somewhere else:  ROOT = r"D:\\code\\Physalia"
try:
    ROOT
except NameError:
    ROOT = r"C:\Users\rober\repos\Physalia"
exec(open(ROOT + r"\tools\presets\phybuild.py").read())

import glob
import os

hc = _type("Physalia.GH.Harness.HarnessComponent")
readfile = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            if c.Name == "ReadDocumentFile" and c.GetParameters().Length == 2][0]

broken = 0
pairs = 0
for path in sorted(glob.glob(PRESETS + r"\*.phy")):
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
    retire(doc)

say("---- %d Feedback pairs checked, %d broken ----" % (pairs, broken))

# The same class of silent failure applies to every other guid-based link: a Token Count pointing
# at nothing shows no counter, a Set Script I/O pointing at nothing emits no contract, a Delegate
# pointing at nothing advertises no tool. All of them implement IGuidLinked; this proves the remap.
say("")
say("==== grip links ====")
# A Set Script I/O links to EITHER script transmitter -- it reads the target through whichever
# one it points at, and the language is the transmitter's business, not this node's.
LINKED = {"Token Count": ("Token Estimator",),
          "Set Script I/O": ("Py Transmitter", "C# Transmitter"),
          "Delegate": ("Harness",)}
links = 0
for path in sorted(glob.glob(PRESETS + r"\*.phy")):
    name = os.path.basename(path)
    doc = readfile.Invoke(None, System.Array[System.Object]([path, None]))
    if doc is None:
        continue
    found = []

    def links_in(d, where):
        global broken, links
        for o in d.Objects:
            if o.Name in LINKED:
                pr = o.GetType().GetProperty(
                    "LinkedGuid", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                if pr is None:
                    continue
                g = pr.GetValue(o)
                target = d.FindObject(g, False)
                links += 1
                if target is None:
                    found.append("BROKEN %s%s -> nothing" % (where, o.NickName or o.Name))
                    broken += 1
                elif target.Name not in LINKED[o.Name]:
                    found.append("BROKEN %s%s -> a %s, expected one of %s"
                                 % (where, o.NickName or o.Name, target.Name,
                                    " / ".join(LINKED[o.Name])))
                    broken += 1
                else:
                    found.append("ok %s%s -> %s %r"
                                 % (where, o.NickName or o.Name, target.Name, target.NickName))
            if o.Name == "Harness" and o.InnerDocument is not None:
                links_in(o.InnerDocument, where + "inner/")

    links_in(doc, "")
    if found:
        say("%-42s" % name)
        for f in found:
            say("    ", f)
    retire(doc)

say("---- %d grip links checked, %d broken in total ----" % (links, broken))

# ---------------------------------------------------------------------------
# A Router's LAST output is Feedback, not a tool slot. Wiring a tool node's
# Signal to it costs nothing at build time and no sweep can see it: the tool is
# never dispatched and never advertised, so the model is simply told it does not
# exist. Found live on S10, where Pipeline State was one slot past the end.
say("")
slots = 0
misrouted = 0


def routers_in(d, where):
    global slots, misrouted
    for o in d.Objects:
        if o.Name == "Router":
            outs = o.Params.Output
            last = outs.Count - 1
            for i in range(outs.Count):
                for rec in outs[i].Recipients:
                    owner = rec.Attributes.GetTopLevel.DocObject
                    if i == last:
                        if owner.Name not in ("Feedback", "Feedback Collector", "Panel"):
                            say("    MISROUTED %s%s is wired to the Router's FEEDBACK output"
                                % (where, owner.NickName or owner.Name))
                            misrouted += 1
                    else:
                        slots += 1
        if o.Name == "Harness" and o.InnerDocument is not None:
            routers_in(o.InnerDocument, where + "inner/")


for path in sorted(glob.glob(PRESETS + r"/*.phy")):
    doc = readfile.Invoke(None, System.Array[System.Object]([path, None]))
    if doc is None:
        continue
    routers_in(doc, os.path.basename(path)[:24] + " ")
    retire(doc)

say("---- %d router tool slots checked, %d misrouted ----" % (slots, misrouted))

write_log(SCRATCH + r"\check_pairs.log")
