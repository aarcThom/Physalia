# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Which Physalia components does the teaching set actually demonstrate, and which does it not?

Built from the FILES rather than from anyone's memory: every component the plug-in offers, against
every component that appears inside a written .phy. Run it after adding a preset to see what is
still uncovered.

Params are excluded - they are wire types, not things a preset teaches - and so is the Harness
proxy, which every preset is.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

import glob
import os

SKIP_SECTIONS = {"Params"}
SKIP_NAMES = {"Harness"}

# What the plug-in offers, by ribbon section.
offered = {}
for p in Instances.ComponentServer.ObjectProxies:
    if p.Desc.Category != "Physalia":
        continue
    section = p.Desc.SubCategory
    if section in SKIP_SECTIONS or p.Desc.Name in SKIP_NAMES:
        continue
    offered.setdefault(section, set()).add(p.Desc.Name)

# What the presets use.
hc = _type("Physalia.GH.Harness.HarnessComponent")
readfile = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            if c.Name == "ReadDocumentFile" and c.GetParameters().Length == 2][0]

used = {}


def collect(doc, preset):
    for o in doc.Objects:
        used.setdefault(o.Name, set()).add(preset)
        if o.Name == "Harness" and o.InnerDocument is not None:
            collect(o.InnerDocument, preset)


for path in sorted(glob.glob(r"C:\Users\rober\repos\Physalia\wip_presets\*.phy")):
    # The token before " - " -- basename[:2] gave "S0" for every scenario preset, so they all
    # collapsed into one column and the table said nothing about which one covered what.
    preset = os.path.basename(path).split(" - ")[0]
    doc = readfile.Invoke(None, System.Array[System.Object]([path, None]))
    if doc is None:
        say("!! %s unreadable" % preset)
        continue
    collect(doc, preset)
    retire(doc)

total = 0
covered = 0
missing = []
for section in sorted(offered):
    names = sorted(offered[section])
    say("")
    say("%s" % section.upper())
    for name in names:
        total += 1
        where = sorted(used.get(name, []))
        if where:
            covered += 1
            say("  %-32s %s" % (name, " ".join(where)))
        else:
            missing.append((section, name))
            say("  %-32s --" % name)

say("")
say("==== %d of %d components appear in the set; %d do not ====" % (covered, total, len(missing)))
for section, name in missing:
    say("   NOT COVERED  %-14s %s" % (section, name))

write_log(r"C:\Users\rober\AppData\Local\Temp\claude\coverage.log")
