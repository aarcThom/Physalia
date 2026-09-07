# Copyright (c) 2026 Physalia Contributors
# SPDX-License-Identifier: AGPL-3.0-or-later
"""
Audit every written .phy for anything that should not travel: absolute paths, this machine's user
name, a provider or endpoint name that only exists here, or an armed trigger.

A preset is meant to be sent to somebody. Anything in here that is true only of the machine that
wrote it is a defect, whether or not it self-heals on the other end.
"""

exec(open(r"C:\Users\rober\repos\Physalia\tools\presets\phybuild.py").read())

import glob
import os

# Two lists, because the two places have different rules. A PATH or this user's name is wrong
# wherever it appears, prose included. A provider or endpoint name is only wrong as stored
# DATA - in prose it is usually a legitimate example ("Vancouver, Toronto and London all
# publish one"), and flagging that on every build is how a check teaches people to ignore it.
PATHY = ["C:" + chr(92), "/Users/", "rober", "AppData", "repos" + chr(92) + "Physalia"]
MACHINE = PATHY + ["Vancouver Open Data", "deepseek", "tavily"]

hc = _type("Physalia.GH.Harness.HarnessComponent")
readfile = [c for c in hc.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            if c.Name == "ReadDocumentFile" and c.GetParameters().Length == 2][0]

total = 0
for path in sorted(glob.glob(r"C:\Users\rober\repos\Physalia\wip_presets\*.phy")):
    name = os.path.basename(path)
    args = System.Array[System.Object]([path, None])
    doc = readfile.Invoke(None, args)
    if doc is None:
        say("!! %s could not be read" % name)
        total += 1
        continue

    findings = []

    def walk(d, where):
        for o in d.Objects:
            # panels and text params carry typed values
            txt = None
            try:
                txt = o.UserText
            except Exception:
                pass
            if txt:
                for bad in PATHY:
                    if bad.lower() in txt.lower():
                        findings.append("%s panel text mentions %r: %s" % (where, bad, txt[:70]))
            ps = getattr(o, "Params", None)
            if ps is not None:
                for p in ps.Input:
                    pd = getattr(p, "PersistentData", None)
                    try:
                        n = pd.DataCount if pd is not None else 0
                    except Exception:
                        n = 0
                    if n:
                        vals = " ".join(str(v) for v in pd.AllData(True))
                        for bad in MACHINE:
                            if bad.lower() in vals.lower():
                                findings.append("%s %s.%s holds %r: %s"
                                                % (where, o.NickName or o.Name, p.Name, bad, vals[:70]))
            # a trigger that opened armed would start spending on someone else's machine
            if hasattr(o, "IsArmed"):
                try:
                    if o.IsArmed:
                        findings.append("%s %s IS ARMED" % (where, o.NickName or o.Name))
                except Exception:
                    pass
            if o.Name == "Harness":
                nested = o.InnerDocument
                if nested is not None:
                    walk(nested, where + "/inner")

    walk(doc, "")

    # a Picker's saved choice is the other place machine state hides
    def pickers(d, where):
        for o in d.Objects:
            if o.Name != "Picker":
                continue
            pr = o.GetType().GetProperty("SelectedValue",
                                         BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            v = str(pr.GetValue(o)) if pr is not None else "?"
            if v and v not in ("", "None"):
                for bad in MACHINE:
                    if bad.lower() in v.lower():
                        findings.append("%s Picker saved choice %r" % (where, v))
            if o.Name == "Harness":
                pass
        for o in d.Objects:
            if o.Name == "Harness" and o.InnerDocument is not None:
                pickers(o.InnerDocument, where + "/inner")

    pickers(doc, "")

    say("%-42s %s" % (name, "clean" if not findings else "%d finding(s)" % len(findings)))
    for f in findings:
        say("     ", f)
    total += len(findings)
    doc.Dispose()

say("---- total findings:", total)

write_log(r"C:\Users\rober\AppData\Local\Temp\claude\audit.log")
